﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Lime.Protocol.Util
{
#if NET461
    /// <summary>
    /// Used to control the rate of some occurrence per unit of time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     To control the rate of an action using a <see cref="RateGate"/>, 
    ///     code should simply call <see cref="WaitToProceedAsync()"/> prior to 
    ///     performing the action. <see cref="WaitToProceedAsync()"/> will block
    ///     the current thread until the action is allowed based on the rate 
    ///     limit.
    ///     </para>
    ///     <para>
    ///     This class is thread safe. A single <see cref="RateGate"/> instance 
    ///     may be used to control the rate of an occurrence across multiple 
    ///     threads.
    ///     </para>
    /// </remarks>
    public sealed class RateGate : IDisposable
    {
        // Semaphore used to count and limit the number of occurrences per
        // unit time.
        private readonly SemaphoreSlim _semaphore;

        // Times (in millisecond ticks) at which the semaphore should be exited.
        private readonly ConcurrentQueue<int> _exitTimes;

        // Timer used to trigger exiting the semaphore.
        private readonly Timer _exitTimer;

        // Whether this instance is disposed.
        private bool _isDisposed;

        /// <summary>
        /// Number of occurrences allowed per unit of time.
        /// </summary>
        public int Occurrences { get; private set; }

        /// <summary>
        /// The length of the time unit, in milliseconds.
        /// </summary>
        public int TimeUnitMilliseconds { get; private set; }

        /// <summary>
        /// Initializes a <see cref="RateGate"/> with a rate of <paramref name="occurrences"/> 
        /// per <paramref name="timeUnit"/>.
        /// </summary>
        /// <param name="occurrences">Number of occurrences allowed per unit of time.</param>
        /// <param name="timeUnit">Length of the time unit.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// If <paramref name="occurrences"/> or <paramref name="timeUnit"/> is negative.
        /// </exception>
        public RateGate(int occurrences, TimeSpan timeUnit)
        {
            // Check the arguments.
            if (occurrences <= 0)
                throw new ArgumentOutOfRangeException(nameof(occurrences), "Number of occurrences must be a positive integer");
            if (timeUnit != timeUnit.Duration())
                throw new ArgumentOutOfRangeException(nameof(timeUnit), "Time unit must be a positive span of time");
            if (timeUnit >= TimeSpan.FromMilliseconds(UInt32.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeUnit), "Time unit must be less than 2^32 milliseconds");

            Occurrences = occurrences;
            TimeUnitMilliseconds = (int)timeUnit.TotalMilliseconds;

            // Create the semaphore, with the number of occurrences as the maximum count.
            _semaphore = new SemaphoreSlim(Occurrences, Occurrences);

            // Create a queue to hold the semaphore exit times.
            _exitTimes = new ConcurrentQueue<int>();

            // Create a timer to exit the semaphore. Use the time unit as the original
            // interval length because that's the earliest we will need to exit the semaphore.
            _exitTimer = new Timer(ExitTimerCallback, null, TimeUnitMilliseconds, -1);
        }

        ~RateGate()
        {
            Dispose(false);
        }

        // Callback for the exit timer that exits the semaphore based on exit times 
        // in the queue and then sets the timer for the nextexit time.
        private void ExitTimerCallback(object state)
        {
            try
            {
                // While there are exit times that are passed due still in the queue,
                // exit the semaphore and dequeue the exit time.
                int exitTime;
                while (_exitTimes.TryPeek(out exitTime)
                        && unchecked(exitTime - Environment.TickCount) <= 0)
                {
                    _semaphore.Release();
                    _exitTimes.TryDequeue(out exitTime);
                }

                // Try to get the next exit time from the queue and compute
                // the time until the next check should take place. If the 
                // queue is empty, then no exit times will occur until at least
                // one time unit has passed.
                int timeUntilNextCheck;
                if (_exitTimes.TryPeek(out exitTime))
                    timeUntilNextCheck = unchecked(exitTime - Environment.TickCount);
                else
                    timeUntilNextCheck = TimeUnitMilliseconds;

                // Set the timer.
                _exitTimer.Change(timeUntilNextCheck, -1);
            }
            catch (ObjectDisposedException)
            {
                // Do not rethrow or else the process will be shutdown
            }
        }

        /// <summary>
        /// Wait until allowed to proceed or until the
        /// specified timeout elapses.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for the task</param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public async Task WaitToProceedAsync(CancellationToken cancellationToken)
        {
            CheckDisposed();

            // Block until we can enter the semaphore or until the timeout expires.
            await _semaphore.WaitAsync(cancellationToken);

            // Compute the corresponding exit time 
            // and add it to the queue.            
            var timeToExit = unchecked(Environment.TickCount + TimeUnitMilliseconds);
            _exitTimes.Enqueue(timeToExit);
        }

        /// <summary>
        /// Blocks the current thread indefinitely until allowed to proceed.
        /// </summary>
        public Task WaitToProceedAsync()
        {
            return WaitToProceedAsync(CancellationToken.None);
        }

        // Throws an ObjectDisposedException if this object is disposed.
        private void CheckDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException("RateGate is already disposed");
        }

        /// <summary>
        /// Releases unmanaged resources held by an instance of this class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged resources held by an instance of this class.
        /// </summary>
        /// <param name="isDisposing">Whether this object is being disposed.</param>
        private void Dispose(bool isDisposing)
        {
            if (!_isDisposed)
            {
                if (isDisposing)
                {
                    // The semaphore and timer both implement IDisposable and 
                    // therefore must be disposed.
                    _semaphore.Dispose();                    
                    _exitTimer.Dispose();                    
                    _isDisposed = true;
                }
            }
        }
    }
#endif
}
