// <copyright file="Waiter.cs" company="JP Dillingham">
//     Copyright (c) JP Dillingham.
//     Copyright (c) 2026 AnimaSeek contributors.
//     Modified: Reworked wait completion around typed, reflection-free sources for AOT,
//     preserving upstream exact result-type matching on completion.
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, version 3.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see https://www.gnu.org/licenses/.
//
//     This program is distributed with Additional Terms pursuant to Section 7
//     of the GPLv3.  See the LICENSE file in the root directory of this
//     project for the complete terms and conditions.
//
//     SPDX-FileCopyrightText: JP Dillingham
//     SPDX-FileCopyrightText: 2026 AnimaSeek contributors
//     SPDX-License-Identifier: GPL-3.0-only
// </copyright>

namespace Soulseek
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    ///     Enables await-able server messages.
    /// </summary>
    internal sealed class Waiter : IWaiter
    {
        private const int DefaultTimeoutValue = 5000;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Waiter"/> class with the default timeout.
        /// </summary>
        public Waiter()
            : this(DefaultTimeoutValue)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Waiter"/> class with the specified <paramref name="defaultTimeout"/>.
        /// </summary>
        /// <param name="defaultTimeout">The default timeout duration for message waits.</param>
        public Waiter(int defaultTimeout)
        {
            DefaultTimeout = defaultTimeout;
        }

        /// <summary>
        ///     Gets the default timeout duration, in milliseconds.
        /// </summary>
        public int DefaultTimeout { get; private set; }

        private bool Disposed { get; set; }
        private ConcurrentDictionary<WaitKey, ReaderWriterLockSlim> Locks { get; } = new ConcurrentDictionary<WaitKey, ReaderWriterLockSlim>();
        private ConcurrentDictionary<WaitKey, ConcurrentQueue<PendingWait>> Waits { get; } = new ConcurrentDictionary<WaitKey, ConcurrentQueue<PendingWait>>();

        /// <summary>
        ///     Cancels the oldest wait matching the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The unique WaitKey for the wait.</param>
        public void Cancel(WaitKey key)
        {
            Disposition(key, wait =>
                wait.CompletionSource.TrySetCanceled());
        }

        /// <summary>
        ///     Cancels all waits.
        /// </summary>
        public void CancelAll()
        {
            var keys = Waits.Keys.ToList();

            foreach (var key in keys)
            {
                Cancel(key);
            }
        }

        /// <summary>
        ///     Completes the oldest wait matching the specified <paramref name="key"/> with the specified <paramref name="result"/>.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="key">The unique WaitKey for the wait.</param>
        /// <param name="result">The wait result.</param>
        public void Complete<T>(WaitKey key, T result)
        {
            try
            {
                // the cast below mirrors upstream's TaskCompletionSource<T> cast; because
                // WaitCompletionSource<T> is invariant, it throws InvalidCastException whenever the
                // type specified by Complete() does not exactly match the type specified by Wait(),
                // including Complete(key) (object) against a typed wait.
                Disposition(key, wait =>
                    ((WaitCompletionSource<T>)wait.CompletionSource).TrySetResult(result));
            }
            catch (InvalidCastException ex)
            {
                throw new SoulseekClientException($"Failed to complete the wait for key {key}; the result type specified by Complete() does not match the type specified by Wait().", ex);
            }
        }

        /// <summary>
        ///     Completes the oldest wait matching the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The unique WaitKey for the wait.</param>
        public void Complete(WaitKey key)
        {
            Complete<object>(key, null);
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        /// <param name="disposing">A value indicating whether disposal is in progress.</param>
        public void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    CancelAll();
                }

                Disposed = true;
            }
        }

        /// <summary>
        ///     Returns a value indicating whether the waiter has any waits for the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The unique WaitKey for the wait.</param>
        /// <returns>A value indicating whether any waits exist for the key.</returns>
        public bool HasWait(WaitKey key) => Waits.TryGetValue(key, out _);

        /// <summary>
        ///     Throws the specified <paramref name="exception"/> on the oldest wait matching the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The unique WaitKey for the wait.</param>
        /// <param name="exception">The Exception to throw.</param>
        public void Throw(WaitKey key, Exception exception)
        {
            Disposition(key, wait =>
                wait.CompletionSource.TrySetException(exception));
        }

        /// <summary>
        ///     Causes the oldest wait matching the specified <paramref name="key"/> to time out.
        /// </summary>
        /// <param name="key">The unique WaitKey for the wait.</param>
        public void Timeout(WaitKey key)
        {
            Disposition(key, wait =>
                wait.CompletionSource.TrySetException(new TimeoutException($"The wait timed out after {wait.Timeout} milliseconds")));
        }

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="key"/> and with the specified <paramref name="timeout"/>.
        /// </summary>
        /// <param name="key">A unique WaitKey for the wait.</param>
        /// <param name="timeout">The wait timeout, in milliseconds.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        public Task Wait(WaitKey key, int? timeout = null, CancellationToken? cancellationToken = null)
        {
            return Wait<object>(key, timeout, cancellationToken);
        }

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="key"/> and with the specified <paramref name="timeout"/>.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="key">A unique WaitKey for the wait.</param>
        /// <param name="timeout">The wait timeout, in milliseconds.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        public Task<T> Wait<T>(WaitKey key, int? timeout = null, CancellationToken? cancellationToken = null)
        {
            timeout ??= DefaultTimeout;
            cancellationToken ??= CancellationToken.None;

            var completionSource = new WaitCompletionSource<T>();

            var wait = new PendingWait(
                completionSource,
                timeout.Value,
                cancelAction: () => Cancel(key),
                timeoutAction: () => Timeout(key),
                cancellationToken.Value);

            // obtain a read lock for the key. this is necessary to prevent this code from adding a wait to the ConcurrentQueue
            // while the containing dictionary entry is being cleaned up in Disposition(), effectively discarding the new wait.
            var recordLock = Locks.GetOrAdd(key, new ReaderWriterLockSlim());

            recordLock.EnterReadLock();

            try
            {
                Waits.AddOrUpdate(key, new ConcurrentQueue<PendingWait>(new[] { wait }), (_, queue) =>
                {
                    queue.Enqueue(wait);
                    return queue;
                });
            }
            finally
            {
                recordLock.ExitReadLock();
            }

            // defer registration to prevent the wait from being dispositioned prior to being successfully queued this is a
            // concern if we are given a timeout of 0, or a cancellation token which is already cancelled
            wait.Register();
            return completionSource.Task;
        }

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="key"/> which does not time out.
        /// </summary>
        /// <param name="key">A unique WaitKey for the wait.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        public Task WaitIndefinitely(WaitKey key, CancellationToken? cancellationToken = null)
        {
            return WaitIndefinitely<object>(key, cancellationToken);
        }

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="key"/> which does not time out.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="key">A unique WaitKey for the wait.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        public Task<T> WaitIndefinitely<T>(WaitKey key, CancellationToken? cancellationToken = null)
        {
            return Wait<T>(key, int.MaxValue, cancellationToken);
        }

        private void Disposition(WaitKey key, Action<PendingWait> action)
        {
            if (Waits.TryGetValue(key, out var queue) && queue.TryDequeue(out var wait))
            {
                try
                {
                    action(wait);
                }
                finally
                {
                    wait.Dispose();
                    Cleanup(key, queue);
                }
            }
        }

        private void Cleanup(WaitKey key, ConcurrentQueue<PendingWait> queue)
        {
            if (Locks.TryGetValue(key, out var recordLock))
            {
                // enter a read lock first; TryPeek and TryDequeue are atomic so there's no risky operation until later.
                recordLock.EnterUpgradeableReadLock();

                try
                {
                    // clean up entries in the Waits and Locks dictionaries if the corresponding ConcurrentQueue is empty.
                    // this is tricky, because we don't want to remove a record if another thread is in the process of
                    // enqueueing a new wait.
                    if (queue.IsEmpty)
                    {
                        // enter the write lock to prevent Wait() (which obtains a read lock) from enqueing any more waits
                        // before we can delete the dictionary record. it's ok and expected that Wait() might add this record
                        // back to the dictionary as soon as this unblocks; we're preventing new waits from being discarded if
                        // they are added by another thread just prior to the TryRemove() operation below.
                        recordLock.EnterWriteLock();

                        try
                        {
                            // check the queue again to ensure Wait() didn't enqueue anything between the last check and when
                            // we entered the write lock. this is guarateed to be safe since we now have exclusive access to
                            // the record and it should be impossible to remove a record containing a non-empty queue
                            if (queue.IsEmpty)
                            {
                                Waits.TryRemove(key, out _);
                                Locks.TryRemove(key, out _);
                            }
                        }
                        finally
                        {
                            recordLock.ExitWriteLock();
                        }
                    }
                }
                finally
                {
                    recordLock.ExitUpgradeableReadLock();
                }
            }
        }

        /// <summary>
        ///     Adapts a generic task completion source to the non-generic pending-wait abstraction.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        internal sealed class WaitCompletionSource<T> : IWaitCompletionSource
        {
            /// <summary>
            ///     Gets the task completed by this source.
            /// </summary>
            public Task<T> Task => Source.Task;

            /// <summary>
            ///     Gets the typed task completion source wrapped by this instance.
            /// </summary>
            public TaskCompletionSource<T> Source { get; } = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc />
            public bool TrySetCanceled() => Source.TrySetCanceled();

            /// <inheritdoc />
            public bool TrySetException(Exception exception) => Source.TrySetException(exception);

            /// <summary>
            ///     Attempts to transition the wait task to the completed state with the specified <paramref name="result"/>.
            /// </summary>
            /// <param name="result">The result with which to complete the wait.</param>
            /// <returns><see langword="true"/> if the transition succeeded; otherwise, <see langword="false"/>.</returns>
            public bool TrySetResult(T result) => Source.TrySetResult(result);

            /// <inheritdoc />
            /// <remarks>
            ///     Completion type checking is enforced ahead of this call; <see cref="Waiter.Complete{T}(WaitKey, T)"/>
            ///     casts to the exact <see cref="WaitCompletionSource{T}"/> instantiation and invokes
            ///     <see cref="TrySetResult(T)"/> directly, matching upstream semantics.  This non-generic path retains
            ///     only the runtime cast for any other <see cref="IWaitCompletionSource"/> consumer.
            /// </remarks>
            bool IWaitCompletionSource.TrySetResult(object result) => TrySetResult((T)result);
        }

        /// <summary>
        ///     The composite value for the wait dictionary.
        /// </summary>
        internal class PendingWait : IDisposable
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="PendingWait"/> class.
            /// </summary>
            /// <param name="completionSource">The completion source for the wait task.</param>
            /// <param name="timeout">The number of milliseconds after which the wait is to time out.</param>
            /// <param name="cancelAction">The action to invoke when the task is cancelled.</param>
            /// <param name="timeoutAction">The action to invoke when the task times out.</param>
            /// <param name="cancellationToken">The cancellation token for the wait.</param>
            public PendingWait(IWaitCompletionSource completionSource, int timeout, Action cancelAction, Action timeoutAction, CancellationToken cancellationToken)
            {
                CompletionSource = completionSource;
                Timeout = timeout;
                CancelAction = cancelAction;
                TimeoutAction = timeoutAction;
                CancellationToken = cancellationToken;
            }

            /// <summary>
            ///     Gets the completion source for the wait task.
            /// </summary>
            public IWaitCompletionSource CompletionSource { get; }

            /// <summary>
            ///     Gets the number of milliseconds after which the wait is to time out.
            /// </summary>
            public int Timeout { get; }

            private Action CancelAction { get; set; }
            private CancellationToken CancellationToken { get; set; }
            private CancellationTokenRegistration CancellationTokenRegistration { get; set; }
            private bool Disposed { get; set; }
            private Action TimeoutAction { get; set; }
            private CancellationTokenRegistration TimeoutTokenRegistration { get; set; }
            private CancellationTokenSource TimeoutTokenSource { get; set; }

            /// <summary>
            ///     Releases the managed and unmanaged resources used by the <see cref="PendingWait"/>.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            ///     Register cancellation and timeout actions.
            /// </summary>
            public void Register()
            {
                CancellationTokenRegistration = CancellationToken.Register(() => CancelAction());

                TimeoutTokenSource = new CancellationTokenSource(Timeout);
                TimeoutTokenRegistration = TimeoutTokenSource.Token.Register(() => TimeoutAction());
            }

            /// <summary>
            ///     Releases the managed and unmanaged resources used by the <see cref="PendingWait"/>.
            /// </summary>
            /// <param name="disposing">A value indicating whether the object is being disposed.</param>
            protected virtual void Dispose(bool disposing)
            {
                if (!Disposed)
                {
                    if (disposing)
                    {
                        // this will be null if the wait is disposed before Register() is called,
                        // which can happen if a transfer fails very fast (e.g. if the remote client rejects it)
                        TimeoutTokenSource?.Dispose();

                        CancellationTokenRegistration.Dispose();
                        TimeoutTokenRegistration.Dispose();
                    }

                    Disposed = true;
                }
            }
        }
    }
}
