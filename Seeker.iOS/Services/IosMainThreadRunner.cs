using CoreFoundation;
using Foundation;
using Seeker.Services;

namespace AnimaSeek.iOS.Services;

/// <summary>Marshals portable UI work to the process-wide main dispatch queue.</summary>
internal sealed class IosMainThreadRunner : IMainThreadRunner
{
    /// <inheritdoc/>
    public void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (NSThread.IsMain)
        {
            action();
            return;
        }

        DispatchQueue.MainQueue.DispatchAsync(action);
    }
}
