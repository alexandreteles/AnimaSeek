using System;
using Soulseek;

namespace Seeker.Services
{
    /// <summary>Classifies authenticated Soulseek connection loss for automatic foreground recovery.</summary>
    public static class ReconnectDisconnectClassifier
    {
        /// <summary>
        /// Returns whether a state transition represents an unexpected loss of an authenticated server session.
        /// </summary>
        /// <param name="args">The Soulseek state transition.</param>
        /// <param name="intentionalDisconnect">
        /// Whether the application initiated this transition for logout, backgrounding, account replacement, or a
        /// bounded background operation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> only for an authenticated-to-disconnecting/disconnected transition that was not
        /// intentional, caused by a server kick, or caused by disposal.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
        public static bool IsUnexpected(
            SoulseekClientStateChangedEventArgs args,
            bool intentionalDisconnect)
        {
            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            bool wasAuthenticated =
                args.PreviousState.HasFlag(SoulseekClientStates.Connected) &&
                args.PreviousState.HasFlag(SoulseekClientStates.LoggedIn);
            bool lostAuthentication =
                !args.State.HasFlag(SoulseekClientStates.LoggedIn) &&
                (args.State.HasFlag(SoulseekClientStates.Disconnecting) ||
                 args.State.HasFlag(SoulseekClientStates.Disconnected));

            return wasAuthenticated &&
                   lostAuthentication &&
                   !intentionalDisconnect &&
                   args.Exception is not KickedFromServerException &&
                   args.Exception is not ObjectDisposedException;
        }
    }
}
