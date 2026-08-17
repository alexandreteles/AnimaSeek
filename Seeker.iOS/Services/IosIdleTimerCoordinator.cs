using Common;
using Seeker;
using Seeker.Services;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.Services;

/// <summary>Keeps the screen awake only while the opt-in preference, active scene, and live transfer all agree.</summary>
internal sealed class IosIdleTimerCoordinator : IDisposable
{
    private readonly AppSession session;
    private readonly IKeyValueStore keyValueStore;
    private readonly IMainThreadRunner mainThreadRunner;
    private bool sceneActive;
    private bool disposed;

    /// <summary>Creates a coordinator over the process session and stored opt-in.</summary>
    public IosIdleTimerCoordinator(
        AppSession session,
        IKeyValueStore keyValueStore,
        IMainThreadRunner mainThreadRunner)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
        session.TransfersChanged += OnTransfersChanged;
    }

    /// <summary>Updates the scene activity input and immediately reconciles <see cref="UIApplication.IdleTimerDisabled"/>.</summary>
    public void SetSceneActive(bool active)
    {
        if (disposed)
        {
            return;
        }

        sceneActive = active;
        Reconcile();
    }

    /// <summary>Persists the user opt-in and immediately applies it to current transfers.</summary>
    public void SetKeepAwakeEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        PreferencesState.KeepScreenAwakeWhileTransferring = enabled;
        keyValueStore.PutBoolean(KeyConsts.M_KeepScreenAwakeWhileTransferring, enabled);
        keyValueStore.Flush();
        Reconcile();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sceneActive = false;
        session.TransfersChanged -= OnTransfersChanged;
        SetIdleTimerDisabled(false);
    }

    private void OnTransfersChanged(object? sender, EventArgs args) => Reconcile();

    private void Reconcile() =>
        SetIdleTimerDisabled(
            sceneActive &&
            PreferencesState.KeepScreenAwakeWhileTransferring &&
            session.HasActiveOrQueuedTransfers);

    private void SetIdleTimerDisabled(bool disabled) =>
        mainThreadRunner.RunOnUiThread(() => UIApplication.SharedApplication.IdleTimerDisabled = disabled);
}
