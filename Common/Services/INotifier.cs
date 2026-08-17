namespace Seeker.Services
{
    /// <summary>
    /// Publishes semantic application notifications through the current platform's notification service.
    /// </summary>
    /// <remarks>
    /// Implementations own permission handling, presentation, localization, and thread marshalling. A denied or
    /// unavailable notification permission should be treated as a no-op rather than an application error. Methods
    /// may be called from background threads.
    /// </remarks>
    public interface INotifier
    {
        /// <summary>
        /// Posts a notification and may alert the user according to platform and user settings.
        /// </summary>
        /// <param name="notification">The semantic notification to post.</param>
        void Post(SemanticNotification notification);

        /// <summary>
        /// Replaces the notification with the same <see cref="SemanticNotification.Id"/>.
        /// </summary>
        /// <param name="notification">The latest semantic state for the notification.</param>
        /// <remarks>
        /// Implementations should avoid alerting the user again when the identified notification already exists.
        /// If it no longer exists, the implementation may post it silently.
        /// </remarks>
        void Update(SemanticNotification notification);

        /// <summary>
        /// Cancels the pending or delivered notification identified by <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The semantic notification identity.</param>
        /// <remarks>Canceling an unknown identity is a no-op.</remarks>
        void Cancel(NotificationId id);

        /// <summary>
        /// Cancels all pending or delivered notifications of <paramref name="kind"/>.
        /// </summary>
        /// <param name="kind">The semantic notification category to cancel.</param>
        /// <remarks>Canceling a category with no notifications is a no-op.</remarks>
        void Cancel(NotificationKind kind);
    }
}
