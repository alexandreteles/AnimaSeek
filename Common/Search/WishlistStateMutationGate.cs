using System;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Search
{
    /// <summary>
    /// Serializes read-modify-write operations over the canonical wishlist header snapshot.
    /// </summary>
    /// <remarks>
    /// The persistence format stores every wishlist header in one value. Every process service that changes that
    /// value must share one instance of this gate so a prepared settings import cannot overwrite a refresh result,
    /// and a refresh cannot overwrite newly imported entries.
    /// </remarks>
    public sealed class WishlistStateMutationGate
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        /// <summary>Runs a synchronous wishlist mutation while excluding every other mutation using this instance.</summary>
        /// <typeparam name="TResult">The result produced by <paramref name="operation"/>.</typeparam>
        /// <param name="operation">The complete read-modify-write operation to serialize.</param>
        /// <param name="cancellationToken">Cancels while waiting to enter the gate.</param>
        /// <returns>The value returned by <paramref name="operation"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">The wait was cancelled before the operation began.</exception>
        public async Task<TResult> RunAsync<TResult>(
            Func<TResult> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return operation();
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Runs an asynchronous wishlist mutation while excluding every other mutation using this instance.</summary>
        /// <typeparam name="TResult">The result produced by <paramref name="operation"/>.</typeparam>
        /// <param name="operation">The complete asynchronous read-modify-write operation to serialize.</param>
        /// <param name="cancellationToken">Cancels while waiting and is passed to the operation after entry.</param>
        /// <returns>The value returned by <paramref name="operation"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">The wait or operation was cancelled.</exception>
        public async Task<TResult> RunAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
