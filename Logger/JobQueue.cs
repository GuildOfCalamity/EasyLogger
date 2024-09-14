using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Logger
{
    /// <summary>
    /// Represents a thread-safe FIFO collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IJobQueue<T> : IDisposable
    {
        /// <summary>
        /// Event that is raised once the job queue is empty.
        /// </summary>
        event Action<DateTime>? Depleted;

        /// <summary>
        /// Event that is raised when the task was not able to complete.
        /// </summary>
        event Action<Exception>? UnhandledException;

        /// <summary>
        /// Gets the amount of queued items.
        /// </summary>
        long Count { get; }

        /// <summary>
        /// Adds an object to the end of the <see cref="IJobQueue{T}"/> and returns a <see cref="Task"/> that
        /// will be completed when the job will be completed.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <exception cref="ObjectDisposedException" />
        Task Enqueue(T item);

        /// <summary>
        /// Adds an object to the end of the <see cref="IJobQueue{T}"/> and does not propagate exceptions.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <exception cref="ObjectDisposedException" />
        Task EnqueueIgnoreExceptions(T item);

        /// <summary>
        /// Adds an object to the end of the <see cref="IJobQueue{T}"/> via Fire-n-Forget.
        /// Any <see cref="Exception"/>s thrown will not be propagated.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <exception cref="ObjectDisposedException" />
        void TryEnqueue(T item);
    }

    /// <summary>
    /// Represents a thread-safe FIFO collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class JobQueue<T> : IJobQueue<T>
    {
        readonly Func<T, Task> _method;
        readonly SemaphoreSlim _semaphore;
        volatile bool _isDisposed = false;
        long _queuedCount = 0;

        public event Action<DateTime>? Depleted;
        public event Action<Exception>? UnhandledException;

        public long Count 
        { 
            get => Volatile.Read(ref _queuedCount); 
        }

        public JobQueue(int limit, Func<T, Task> method)
        {
            _method = method;
            _semaphore = new SemaphoreSlim(limit);
        }

        public Task Enqueue(T item)
        {
            return Enqueue(item, true);
        }

        public Task EnqueueIgnoreExceptions(T item)
        {
            return Enqueue(item, false);
        }

        private Task Enqueue(T item, bool throwExceptions)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("JobQueue");
            }
            return Run(item, throwExceptions);
        }

        public void TryEnqueue(T item)
        {
            Enqueue(item, false).FireAndForget();
        }

        /// <summary>
        /// Awaits an available slot and starts the async operation.
        /// </summary>
        async Task Run(T item, bool throwExceptions)
        {
            Interlocked.Increment(ref _queuedCount);
            try
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                var task = _method(item);
                await task.ConfigureAwait(false);
                try
                {
                    _semaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // We don't mind if the semaphore has been disposed at this moment.
                    Debug.WriteLine("[WARNING] Semaphore is already disposed.");
                }
            }
            catch (Exception ex)
            {
                UnhandledException?.Invoke(ex);
                if (throwExceptions)
                    throw;
            }
            finally
            {
                var empty = Interlocked.Decrement(ref _queuedCount) == 0;
                if (empty)
                    Depleted?.Invoke(DateTime.Now);
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            try { _semaphore.Dispose(); }
            catch (Exception) { }
        }

    }

    /// <summary>
    /// Task extensions.
    /// </summary>
    internal static class TaskHelper
    {
        internal static async void FireAndForget(this Task task, Action<Exception>? onException = null, bool continueOnCapturedContext = false)
        {
            try
            {
                await task.ConfigureAwait(continueOnCapturedContext);
            }
            catch (Exception ex) when (onException != null)
            {
                onException.Invoke(ex);
            }
            catch (Exception ex) when (onException == null)
            {
                Console.WriteLine($"[ERROR] FireAndForget: {ex.Message}");
            }
        }

        /// <summary>
        /// Task.Factory.StartNew (() => { throw null; }).IgnoreExceptions();
        /// </summary>
        internal static void IgnoreExceptions(this Task task, bool logEx = false)
        {
            task.ContinueWith(t =>
            {
                AggregateException ignore = t.Exception;

                ignore?.Flatten().Handle(ex =>
                {
                    if (logEx)
                        Console.WriteLine($"Exception type: {ex.GetType()}\r\nException Message: {ex.Message}");
                    return true; // don't re-throw
                });

            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Timeout add-on for <see cref="Task"/>.
        /// var result = await SomeLongAsyncFunction().WithTimeout(TimeSpan.FromSeconds(2));
        /// </summary>
        /// <typeparam name="TResult">the type of task result</typeparam>
        /// <returns><see cref="Task"/>TResult</returns>
        internal async static Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(timeout));

            if (winner != task)
                throw new TimeoutException();

            return await task; // Unwrap result/re-throw
        }

        /// <summary>
        /// Timeout add-on for <see cref="Task"/>.
        /// </summary>
        /// <returns>The <see cref="Task"/> with timeout.</returns>
        /// <param name="task">Task.</param>
        /// <param name="timeoutInMilliseconds">Timeout duration in Milliseconds.</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        internal async static Task<T> WithTimeout<T>(this Task<T> task, int timeoutInMilliseconds)
        {
            var retTask = await Task.WhenAny(task, Task.Delay(timeoutInMilliseconds)).ConfigureAwait(false);

            #pragma warning disable CS8603 // Possible null reference return.
            return retTask is Task<T> ? task.Result : default;
            #pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// <see cref="CancellationToken"/> add-on for <see cref="Task"/>.
        /// var result = await SomeLongAsyncFunction().WithCancellation(cts.Token);
        /// </summary>
        /// <typeparam name="TResult">the type of task result</typeparam>
        /// <returns><see cref="Task"/>TResult</returns>
        internal static Task<TResult> WithCancellation<TResult>(this Task<TResult> task, CancellationToken cancelToken)
        {
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            CancellationTokenRegistration reg = cancelToken.Register(() => tcs.TrySetCanceled());
            task.ContinueWith(ant =>
            {
                reg.Dispose(); // NOTE: it's important to dispose of CancellationTokenRegistrations or they will hang around in memory until the application closes
                if (ant.IsCanceled)
                    tcs.TrySetCanceled();
                else if (ant.IsFaulted)
                    tcs.TrySetException(ant.Exception.InnerException);
                else
                    tcs.TrySetResult(ant.Result);
            });
            return tcs.Task;  // Return the TaskCompletionSource result
        }

        internal static Task<T> WithAllExceptions<T>(this Task<T> task)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

            task.ContinueWith(ignored =>
            {
                switch (task.Status)
                {
                    case TaskStatus.Canceled:
                        Console.WriteLine($"[TaskStatus.Canceled]");
                        tcs.SetCanceled();
                        break;
                    case TaskStatus.RanToCompletion:
                        tcs.SetResult(task.Result);
                        //Console.WriteLine($"[TaskStatus.RanToCompletion({task.Result})]");
                        break;
                    case TaskStatus.Faulted:
                        // SetException will automatically wrap the original AggregateException
                        // in another one. The new wrapper will be removed in TaskAwaiter, leaving
                        // the original intact.
                        Console.WriteLine($"[TaskStatus.Faulted]: {task.Exception.Message}");
                        tcs.SetException(task.Exception);
                        break;
                    default:
                        Console.WriteLine($"[TaskStatus: Continuation called illegally.]");
                        tcs.SetException(new InvalidOperationException("Continuation called illegally."));
                        break;
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Bandwidth helper for <see cref="IEnumerable{Func{TResult}}"/>
        /// </summary>
        /// <param name="toRun"></param>
        /// <param name="throttleTo"></param>
        /// <returns><see cref="Task"/></returns>
        internal static async Task Throttle(this IEnumerable<Func<Task>> toRun, int throttleTo)
        {
            var running = new List<Task>(throttleTo);
            foreach (var taskToRun in toRun)
            {
                running.Add(taskToRun());
                if (running.Count == throttleTo)
                {
                    var comTask = await Task.WhenAny(running);
                    running.Remove(comTask);
                }
            }
        }

        /// <summary>
        /// Bandwidth  helper for <see cref="IEnumerable{Func{Task{T}}}"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toRun"></param>
        /// <param name="throttleTo"></param>
        /// <returns><see cref="Task{IEnumerable{T}}"/></returns>
        internal static async Task<IEnumerable<T>> Throttle<T>(this IEnumerable<Func<Task<T>>> toRun, int throttleTo)
        {
            var running = new List<Task<T>>(throttleTo);
            var completed = new List<Task<T>>(toRun.Count());
            foreach (var taskToRun in toRun)
            {
                running.Add(taskToRun());
                if (running.Count == throttleTo)
                {
                    var comTask = await Task.WhenAny(running);
                    running.Remove(comTask);
                    completed.Add(comTask);
                }
            }
            return completed.Select(t => t.Result);
        }

        /// <summary>
        /// LINQ extension to be able to fluently wait for all of <see cref="IEnumerable{T}" /> of <see cref="Task" /> 
        /// just like <see cref="Task.WhenAll(System.Threading.Tasks.Task[])" />.
        /// </summary>
        /// <param name="tasks">The tasks.</param>
        /// <returns>An awaitable task</returns>
        /// <example><code>
        /// var something = await foos.Select(foo => BarAsync(foo)).WhenAll();
        /// </code></example>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentException"></exception>
        internal static Task WhenAll(this IEnumerable<Task> tasks)
        {
            var enumeratedTasks = tasks as Task[] ?? tasks?.ToArray();

            return Task.WhenAll(enumeratedTasks);
        }

        /// <summary>
        /// LINQ extension to be able to fluently wait for any of <see cref="IEnumerable{T}" /> of <see cref="Task" /> 
        /// just like <see cref="Task.WhenAll(System.Threading.Tasks.Task[])" />.
        /// </summary>
        /// <param name="tasks">The tasks.</param>
        /// <returns>An awaitable task</returns>
        /// <example><code>
        /// var something = await foos.Select(foo => BarAsync(foo)).WhenAll();
        /// </code></example>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentException"></exception>
        internal static Task WhenAny(this IEnumerable<Task> tasks)
        {
            var enumeratedTasks = tasks as Task[] ?? tasks.ToArray();

            return Task.WhenAny(enumeratedTasks);
        }

        /// <summary>
        /// LINQ extension to be able to fluently wait for all of <see cref="IEnumerable{T}" /> of <see cref="Task" /> 
        /// just like <see cref="Task.WhenAll(System.Threading.Tasks.Task{TResult}[])" />.
        /// </summary>
        /// <param name="tasks">The tasks.</param>
        /// <returns>An awaitable task</returns>
        /// <example><code>
        /// var bars = await foos.Select(foo => BarAsync(foo)).WhenAll();
        /// </code></example>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentException"></exception>
        internal static async Task<IEnumerable<TResult>> WhenAll<TResult>(this IEnumerable<Task<TResult>> tasks)
        {
            var enumeratedTasks = tasks as Task<TResult>[] ?? tasks.ToArray();
            var result = await Task.WhenAll(enumeratedTasks);
            return result;
        }

        /// <summary>
        /// LINQ extension to be able to fluently wait for all of <see cref="IEnumerable{T}" /> of <see cref="Task" /> just
        /// like <see cref="Task.WhenAny(System.Threading.Tasks.Task{TResult}[])" />.
        /// </summary>
        /// <param name="tasks">The tasks.</param>
        /// <returns>An awaitable task</returns>
        /// <example><code>
        /// var bar = await foos.Select(foo => BarAsync(foo)).WhenAll();
        /// </code></example>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentException"></exception>
        internal static async Task<TResult> WhenAny<TResult>(this IEnumerable<Task<TResult>> tasks)
        {
            var enumeratedTasks = tasks as Task<TResult>[] ?? tasks.ToArray();
            var result = await await Task.WhenAny(enumeratedTasks);
            return result;
        }

        /// <summary>
        /// <see cref="SemaphoreSlim"/> helper.
        /// </summary>
        /// <param name="ss"></param>
        /// <returns><see cref="Task{TResult}"/></returns>
        internal static async Task<int> EnterAsync(this SemaphoreSlim ss)
        {
            await ss.WaitAsync().ConfigureAwait(false);
            return ss.Release();
        }
    }
}
