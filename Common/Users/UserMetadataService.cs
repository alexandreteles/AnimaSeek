using System.Collections.Concurrent;

namespace Seeker
{
    /// <summary>Holds portable per-user notes, online-alert flags, and recent-user state.</summary>
    public static class UserMetadataService
    {
        /// <summary>Gets or sets notes keyed by user name.</summary>
        public static ConcurrentDictionary<string, string> UserNotes { get; set; } =
            new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Gets or sets the online-alert set represented as a dictionary because the target frameworks do not
        /// provide a concurrent hash set.
        /// </summary>
        public static ConcurrentDictionary<string, byte> UserOnlineAlerts { get; set; } =
            new ConcurrentDictionary<string, byte>();

        /// <summary>Gets or sets the recent-user manager for the active application session.</summary>
        public static RecentUserManager RecentUsersManager { get; set; } = new RecentUserManager();
    }
}
