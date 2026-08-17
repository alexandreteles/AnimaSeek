using Common;
using Seeker.Services;

namespace AnimaSeek.iOS.Services;

/// <summary>Implements the portable reconnect-then-run seam over the iOS application session.</summary>
internal sealed class IosSessionService : ISessionService
{
    private readonly AppSession session;
    private readonly IMainThreadRunner mainThreadRunner;

    /// <summary>Creates the session adapter.</summary>
    /// <param name="session">The application session that owns the Soulseek client.</param>
    /// <param name="mainThreadRunner">The service used to deliver actions safely to UI consumers.</param>
    public IosSessionService(AppSession session, IMainThreadRunner mainThreadRunner)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
    }

    /// <inheritdoc/>
    public bool RunWithReconnect(Action action, bool silent = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (session.IsConnected)
        {
            action();
            return true;
        }

        if (!session.HasStoredCredentials)
        {
            if (!silent)
            {
                session.Toaster.ShowToastShort(StringResources.Get("IosUiSignInToContinue"));
            }

            return false;
        }

        _ = ReconnectAndRunAsync(action, silent);
        return true;
    }

    private async Task ReconnectAndRunAsync(Action action, bool silent)
    {
        try
        {
            await session.ReconnectAsync();
            mainThreadRunner.RunOnUiThread(action);
        }
        catch (Exception exception)
        {
            session.Logger.FirebaseError("Reconnect failed.", exception);
            if (!silent)
            {
                session.Toaster.ShowToastShort(StringResources.Get("IosUiReconnectFailed"));
            }
        }
    }
}
