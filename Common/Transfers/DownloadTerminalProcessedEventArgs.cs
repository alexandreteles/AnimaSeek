using System;

namespace Seeker.Transfers
{
    /// <summary>Describes a download whose file lifecycle has reached a durable terminal boundary.</summary>
    public sealed class DownloadTerminalProcessedEventArgs : EventArgs
    {
        /// <summary>Creates terminal-processing event data.</summary>
        /// <param name="transferItem">The portable transfer item whose finalization or cleanup completed.</param>
        /// <param name="disposition">The terminal operation that completed.</param>
        public DownloadTerminalProcessedEventArgs(
            TransferItem transferItem,
            DownloadTerminalDisposition disposition)
        {
            TransferItem = transferItem ?? throw new ArgumentNullException(nameof(transferItem));
            Disposition = disposition;
        }

        /// <summary>Gets the portable transfer item whose file lifecycle completed.</summary>
        public TransferItem TransferItem { get; }

        /// <summary>Gets the durable success, cleanup, or exhausted-failure disposition.</summary>
        public DownloadTerminalDisposition Disposition { get; }
    }

    /// <summary>Describes a peer-authoritative correction to a download's expected byte length.</summary>
    public sealed class DownloadExpectedSizeChangedEventArgs : EventArgs
    {
        /// <summary>Creates expected-size correction event data.</summary>
        /// <param name="transferItem">The download whose peer-reported size changed.</param>
        /// <param name="expectedSize">The corrected, non-negative byte length reported by the peer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="transferItem"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedSize"/> is negative.</exception>
        public DownloadExpectedSizeChangedEventArgs(TransferItem transferItem, long expectedSize)
        {
            TransferItem = transferItem ?? throw new ArgumentNullException(nameof(transferItem));
            if (expectedSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedSize), expectedSize, "Expected size cannot be negative.");
            }

            ExpectedSize = expectedSize;
        }

        /// <summary>Gets the download whose expected byte length changed.</summary>
        public TransferItem TransferItem { get; }

        /// <summary>Gets the peer-authoritative expected byte length.</summary>
        public long ExpectedSize { get; }
    }

    /// <summary>Orders a durable expected-size write ahead of the corresponding in-memory mutation.</summary>
    public static class DownloadExpectedSizeDurabilityPolicy
    {
        /// <summary>
        /// Runs <paramref name="persist"/> before <paramref name="apply"/> so a process interruption can never expose
        /// the corrected in-memory size without first recording the authoritative crash-resume descriptor.
        /// </summary>
        /// <param name="persist">Durably records the corrected size. Exceptions intentionally propagate.</param>
        /// <param name="apply">Applies the size to volatile transfer state after persistence succeeds.</param>
        /// <exception cref="ArgumentNullException">Either callback is <see langword="null"/>.</exception>
        /// <remarks>If persistence throws, <paramref name="apply"/> is not invoked.</remarks>
        public static void PersistBeforeApplying(Action persist, Action apply)
        {
            if (persist is null)
            {
                throw new ArgumentNullException(nameof(persist));
            }

            if (apply is null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            persist();
            apply();
        }
    }

    /// <summary>Identifies the durable terminal operation completed for a download.</summary>
    public enum DownloadTerminalDisposition
    {
        /// <summary>The complete file was successfully moved into its final destination.</summary>
        Succeeded,

        /// <summary>An explicit cancel-and-clear operation removed the retained partial file.</summary>
        Cleared,

        /// <summary>The retry policy was exhausted and the download reached a non-retriable failed state.</summary>
        Failed,
    }
}
