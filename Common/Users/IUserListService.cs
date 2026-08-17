using Soulseek;

namespace Seeker
{
    public interface IUserListService
    {
        bool ContainsUser(string username);
        /// <summary>Updates data already associated with a friend without adding a new list entry.</summary>
        /// <param name="username">The existing friend name.</param>
        /// <param name="userData">New statistics and presence data, or <see langword="null"/> to preserve it.</param>
        /// <param name="userStatus">New presence data, or <see langword="null"/> to preserve it.</param>
        /// <param name="transitionedOfflineToOnline">
        /// Set to <see langword="true"/> only when an existing friend changes from offline to away or online.
        /// </param>
        /// <returns><see langword="true"/> when the friend existed and was updated.</returns>
        bool UpdateExistingUser(
            string username,
            UserData? userData,
            UserStatus? userStatus,
            out bool transitionedOfflineToOnline);
        bool SetDoesNotExist(string username);
        bool AddUser(UserData userData, UserPresence? status = null);
        bool RemoveUser(string username);
        bool AddToIgnoreList(string username);
        bool RemoveFromIgnoreList(string username);
        bool IsUserInIgnoreList(string username);
    }
}
