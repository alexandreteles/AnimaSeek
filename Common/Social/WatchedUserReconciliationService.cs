using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Seeker.Social
{
    /// <summary>Identifies the server subscription operation that failed during friend-list reconciliation.</summary>
    public enum WatchedUserReconciliationOperation
    {
        /// <summary>The service could not subscribe to a newly added friend.</summary>
        Watch,

        /// <summary>The service could not unsubscribe from a removed or newly ignored friend.</summary>
        Unwatch,
    }

    /// <summary>Describes one isolated server subscription failure.</summary>
    public sealed class WatchedUserReconciliationFailure
    {
        /// <summary>Creates a failure for one user and operation.</summary>
        public WatchedUserReconciliationFailure(
            string username,
            WatchedUserReconciliationOperation operation,
            Exception exception)
        {
            Username = username;
            Operation = operation;
            Exception = exception;
        }

        /// <summary>Gets the affected Soulseek user name.</summary>
        public string Username { get; }

        /// <summary>Gets the attempted subscription operation.</summary>
        public WatchedUserReconciliationOperation Operation { get; }

        /// <summary>Gets the failure returned by the Soulseek client.</summary>
        public Exception Exception { get; }
    }

    /// <summary>Reports successful and failed operations from one server subscription reconciliation.</summary>
    public sealed class WatchedUserReconciliationResult
    {
        /// <summary>Creates an immutable reconciliation result.</summary>
        public WatchedUserReconciliationResult(
            IReadOnlyList<UserData> watchedUsers,
            IReadOnlyList<string> unwatchedUsers,
            IReadOnlyList<WatchedUserReconciliationFailure> failures)
        {
            WatchedUsers = watchedUsers;
            UnwatchedUsers = unwatchedUsers;
            Failures = failures;
        }

        /// <summary>Gets server data returned for newly watched friends, in deterministic user-name order.</summary>
        public IReadOnlyList<UserData> WatchedUsers { get; }

        /// <summary>Gets successfully unwatched user names, in deterministic order.</summary>
        public IReadOnlyList<string> UnwatchedUsers { get; }

        /// <summary>Gets isolated failures; local friend and ignore persistence remains committed.</summary>
        public IReadOnlyList<WatchedUserReconciliationFailure> Failures { get; }
    }

    /// <summary>
    /// Reconciles the connected server watch list after a durable local friend/ignore-list import.
    /// </summary>
    public static class WatchedUserReconciliationService
    {
        /// <summary>
        /// Watches newly added friends and unwatches friends removed by the import, including newly ignored users.
        /// </summary>
        /// <param name="client">The connected Soulseek client whose ephemeral watch list must be updated.</param>
        /// <param name="previousFriendNames">The canonical friend names immediately before the import.</param>
        /// <param name="currentFriendNames">The canonical friend names after the import was durably committed.</param>
        /// <param name="cancellationToken">Cancels all outstanding server operations.</param>
        /// <returns>
        /// Successful operations and isolated failures. Ordinary server failures are returned instead of thrown because
        /// they cannot roll back the already durable local import; cancellation still propagates.
        /// </returns>
        public static async Task<WatchedUserReconciliationResult> ReconcileAsync(
            ISoulseekClient client,
            IEnumerable<string> previousFriendNames,
            IEnumerable<string> currentFriendNames,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (previousFriendNames == null)
            {
                throw new ArgumentNullException(nameof(previousFriendNames));
            }

            if (currentFriendNames == null)
            {
                throw new ArgumentNullException(nameof(currentFriendNames));
            }

            string[] previous = Normalize(previousFriendNames);
            string[] current = Normalize(currentFriendNames);
            string[] toWatch = current.Except(previous, StringComparer.OrdinalIgnoreCase).ToArray();
            string[] toUnwatch = previous.Except(current, StringComparer.OrdinalIgnoreCase).ToArray();

            Task<WatchAttempt>[] watchTasks = toWatch
                .Select(username => TryWatchAsync(client, username, cancellationToken))
                .ToArray();
            Task<UnwatchAttempt>[] unwatchTasks = toUnwatch
                .Select(username => TryUnwatchAsync(client, username, cancellationToken))
                .ToArray();
            Task<WatchAttempt[]> watching = Task.WhenAll(watchTasks);
            Task<UnwatchAttempt[]> unwatching = Task.WhenAll(unwatchTasks);
            await Task.WhenAll(watching, unwatching).ConfigureAwait(false);
            WatchAttempt[] watchAttempts = await watching.ConfigureAwait(false);
            UnwatchAttempt[] unwatchAttempts = await unwatching.ConfigureAwait(false);

            UserData[] watched = watchAttempts
                .Where(attempt => attempt.UserData != null)
                .Select(attempt => attempt.UserData!)
                .ToArray();
            string[] unwatched = unwatchAttempts
                .Where(attempt => attempt.Exception == null)
                .Select(attempt => attempt.Username)
                .ToArray();
            WatchedUserReconciliationFailure[] failures = watchAttempts
                .Where(attempt => attempt.Exception != null)
                .Select(attempt => new WatchedUserReconciliationFailure(
                    attempt.Username,
                    WatchedUserReconciliationOperation.Watch,
                    attempt.Exception!))
                .Concat(unwatchAttempts
                    .Where(attempt => attempt.Exception != null)
                    .Select(attempt => new WatchedUserReconciliationFailure(
                        attempt.Username,
                        WatchedUserReconciliationOperation.Unwatch,
                        attempt.Exception!)))
                .ToArray();

            return new WatchedUserReconciliationResult(watched, unwatched, failures);
        }

        private static string[] Normalize(IEnumerable<string> names) => names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();

        private static async Task<WatchAttempt> TryWatchAsync(
            ISoulseekClient client,
            string username,
            CancellationToken cancellationToken)
        {
            try
            {
                UserData data = await client.WatchUserAsync(username, cancellationToken).ConfigureAwait(false);
                return new WatchAttempt(username, data, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new WatchAttempt(username, null, exception);
            }
        }

        private static async Task<UnwatchAttempt> TryUnwatchAsync(
            ISoulseekClient client,
            string username,
            CancellationToken cancellationToken)
        {
            try
            {
                await client.UnwatchUserAsync(username, cancellationToken).ConfigureAwait(false);
                return new UnwatchAttempt(username, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new UnwatchAttempt(username, exception);
            }
        }

        private sealed class WatchAttempt
        {
            public WatchAttempt(string username, UserData? userData, Exception? exception)
            {
                Username = username;
                UserData = userData;
                Exception = exception;
            }

            public string Username { get; }

            public UserData? UserData { get; }

            public Exception? Exception { get; }
        }

        private sealed class UnwatchAttempt
        {
            public UnwatchAttempt(string username, Exception? exception)
            {
                Username = username;
                Exception = exception;
            }

            public string Username { get; }

            public Exception? Exception { get; }
        }
    }
}
