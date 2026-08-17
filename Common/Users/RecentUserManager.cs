using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker
{
    /// <summary>Maintains the most-recently-used user list independently of any platform persistence API.</summary>
    public sealed class RecentUserManager
    {
        private readonly object recentUserLock = new object();
        private readonly object persistLock = new object();
        private readonly Action<IReadOnlyList<string>>? persist;
        private List<string> recentUsers = new List<string>();
        private long version;
        private long lastPersistedVersion;

        /// <summary>Creates a manager with an optional persistence callback.</summary>
        /// <param name="persist">
        /// An optional callback invoked with a detached snapshot after a mutating operation requests persistence.
        /// Invocations are serialized and the snapshot is re-read at persist time, so the last invocation always
        /// carries the newest state; a request whose state was already covered by a newer persist is dropped.
        /// The callback must not synchronously wait on another thread's persistence request.
        /// </param>
        public RecentUserManager(Action<IReadOnlyList<string>>? persist = null)
        {
            this.persist = persist;
        }

        /// <summary>Replaces the current list with a detached copy of <paramref name="users"/>.</summary>
        /// <param name="users">The ordered recent-user values to restore.</param>
        /// <exception cref="ArgumentNullException"><paramref name="users"/> is <see langword="null"/>.</exception>
        public void SetRecentUserList(IEnumerable<string> users)
        {
            if (users == null)
            {
                throw new ArgumentNullException(nameof(users));
            }

            lock (recentUserLock)
            {
                recentUsers = users.ToList();
                version++;
            }
        }

        /// <summary>Returns a detached snapshot in most-recent-first order.</summary>
        /// <returns>A list that callers may safely mutate without changing manager state.</returns>
        public List<string> GetRecentUserList()
        {
            lock (recentUserLock)
            {
                return recentUsers.ToList();
            }
        }

        /// <summary>Moves a user to the front of the list, optionally persisting the resulting snapshot.</summary>
        /// <param name="user">The user name to move to the front.</param>
        /// <param name="andSave">Whether to invoke the persistence callback after updating the list.</param>
        /// <exception cref="ArgumentNullException"><paramref name="user"/> is <see langword="null"/>.</exception>
        public void AddUserToTop(string user, bool andSave)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            lock (recentUserLock)
            {
                recentUsers.Remove(user);
                recentUsers.Insert(0, user);
                version++;
            }

            if (andSave && persist != null)
            {
                PersistCurrentSnapshot();
            }
        }

        /// <summary>
        /// Persists the current state under a dedicated lock. The snapshot and its version are re-read after
        /// the lock is acquired, so two concurrent save requests cannot durably write an older snapshot last;
        /// a request whose state was already covered by a newer persist becomes a no-op.
        /// </summary>
        private void PersistCurrentSnapshot()
        {
            lock (persistLock)
            {
                long snapshotVersion;
                IReadOnlyList<string> snapshot;
                lock (recentUserLock)
                {
                    snapshotVersion = version;
                    snapshot = recentUsers.ToArray();
                }

                if (snapshotVersion <= lastPersistedVersion)
                {
                    return;
                }

                lastPersistedVersion = snapshotVersion;
                persist!(snapshot);
            }
        }
    }
}
