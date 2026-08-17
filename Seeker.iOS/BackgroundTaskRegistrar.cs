using BackgroundTasks;

namespace AnimaSeek.iOS;

/// <summary>
/// Registers every fixed background-task launch handler during application launch and leases the
/// continued-processing identifiers of that fixed pool while the app runs.
/// </summary>
internal static class BackgroundTaskRegistrar
{
    internal const string TransferIdentifierPrefix = "com.animaseek.app.transfers.";
    internal const string WishlistIdentifier = "com.animaseek.app.wishlist.refresh";
    private const int TransferIdentifierPoolSize = 4;
    private static readonly Lock PoolSync = new();
    private static readonly string[] TransferIdentifierPool = [.. Enumerable
        .Range(0, TransferIdentifierPoolSize)
        .Select(index => $"{TransferIdentifierPrefix}batch{index}")];
    private static readonly HashSet<string> RegisteredTransferIdentifiers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReservedTransferIdentifiers = new(StringComparer.Ordinal);
    private static int nextTransferIdentifierIndex;
    private static IosBackgroundTaskCoordinator? coordinator;
    private static bool registered;

    /// <summary>Gets whether iOS accepted the fixed wishlist task identifier during application launch.</summary>
    public static bool WishlistRegistrationSucceeded { get; private set; }

    /// <summary>Gets how many pooled continued-processing identifiers iOS accepted during application launch.</summary>
    public static int RegisteredTransferIdentifierCount
    {
        get
        {
            lock (PoolSync)
            {
                return RegisteredTransferIdentifiers.Count;
            }
        }
    }

    /// <summary>
    /// Registers the fixed task identifiers that iOS requires before application launch finishes.
    /// </summary>
    /// <remarks>
    /// iOS requires every launch handler to be registered before <c>application:didFinishLaunchingWithOptions:</c>
    /// returns, so the whole continued-processing pool is registered here even though submissions happen later.
    /// Only the *lease* of a pooled identifier is deferred to the moment a foreground batch is submitted; each
    /// handler routes to the coordinator, which accepts a task only while that identifier owns the scheduled batch.
    /// </remarks>
    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        WishlistRegistrationSucceeded = BGTaskScheduler.Shared.Register(
            WishlistIdentifier,
            null,
            task =>
            {
                if (task is BGAppRefreshTask refreshTask && Volatile.Read(ref coordinator) is { } attached)
                {
                    attached.HandleWishlistTask(refreshTask);
                    return;
                }

                task.SetTaskCompleted(false);
            });

        foreach (string identifier in TransferIdentifierPool)
        {
            // Registering the manifest wildcard itself is refused; every concrete pooled identifier it permits
            // needs its own launch handler.
            bool accepted = BGTaskScheduler.Shared.Register(
                identifier,
                null,
                task =>
                {
                    if (task is BGContinuedProcessingTask transferTask && Volatile.Read(ref coordinator) is { } attached)
                    {
                        attached.HandleTransferTask(identifier, transferTask);
                        return;
                    }

                    task.SetTaskCompleted(false);
                });
            if (accepted)
            {
                lock (PoolSync)
                {
                    RegisteredTransferIdentifiers.Add(identifier);
                }
            }
        }
    }

    /// <summary>Attaches the process coordinator after the application service graph is ready.</summary>
    /// <param name="value">The process-wide background task coordinator.</param>
    /// <exception cref="InvalidOperationException">A different coordinator is already attached.</exception>
    public static void Attach(IosBackgroundTaskCoordinator value)
    {
        ArgumentNullException.ThrowIfNull(value);
        IosBackgroundTaskCoordinator? existing = Interlocked.CompareExchange(ref coordinator, value, null);
        if (existing is not null && !ReferenceEquals(existing, value))
        {
            throw new InvalidOperationException("A background task coordinator is already attached.");
        }
    }

    /// <summary>Clears the attached coordinator only when it is the instance being disposed.</summary>
    /// <param name="value">The coordinator releasing process ownership.</param>
    public static void Detach(IosBackgroundTaskCoordinator value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = Interlocked.CompareExchange(ref coordinator, null, value);
    }

    /// <summary>
    /// Leases one registered continued-processing identifier, rotating so a just-released identifier is reused last.
    /// </summary>
    /// <returns>
    /// The reserved identifier, or <see langword="null"/> when every registered slot is in use or launch
    /// registration produced no usable identifier at all.
    /// </returns>
    public static string? ReserveTransferIdentifier()
    {
        lock (PoolSync)
        {
            for (int offset = 0; offset < TransferIdentifierPool.Length; offset++)
            {
                int index = (nextTransferIdentifierIndex + offset) % TransferIdentifierPool.Length;
                string candidate = TransferIdentifierPool[index];
                if (RegisteredTransferIdentifiers.Contains(candidate) &&
                    ReservedTransferIdentifiers.Add(candidate))
                {
                    nextTransferIdentifierIndex = (index + 1) % TransferIdentifierPool.Length;
                    return candidate;
                }
            }

            return null;
        }
    }

    /// <summary>Returns a pooled identifier after its batch completed, expired, or failed to submit.</summary>
    /// <param name="identifier">An identifier previously returned by <see cref="ReserveTransferIdentifier"/>.</param>
    public static void ReleaseTransferIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        lock (PoolSync)
        {
            ReservedTransferIdentifiers.Remove(identifier);
        }
    }
}
