using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Logger;

#region [Supporting Structs]
public enum LogLevel
{
    OFF = 0,
    DEBUG = 1 << 0,   // 2^0 (1)
    INFO = 1 << 1,    // 2^1 (2)
    WARNING = 1 << 2, // 2^2 (4)
    ERROR = 1 << 3,   // 2^3 (8)
    FATAL = 1 << 4,   // 2^4 (16)
}

public struct Message
{
    public LogLevel level;
    public string text;
    public bool time;
    public Message(string value, LogLevel level, bool time)
    {
        this.text = value;
        this.time = time;
        this.level = level;
    }
}
#endregion

#region [Abstract LoggerBase]
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
public abstract class LoggerBase : IDisposable
{
    public event Action<Exception>? OnException;
    protected Encoding _logFileEncoding = Encoding.UTF8;
    protected readonly int _minWait = 5; // milliseconds
    protected string _logFilePath;
    protected string _delimiter ="\t";
    protected string _timeFormat = "yyyy-MM-dd hh:mm:ss.fff tt"; // 2024-08-24 11:30:00.000 AM

    /// <summary>
    /// Assembles the logging path structure for the file.
    /// </summary>
    public virtual string GetLogPath() => Path.Combine(System.AppContext.BaseDirectory, $@"Logs\{DateTime.Today.Year}\{DateTime.Today.Month.ToString("00")}-{DateTime.Today.ToString("MMMM")}");
    public virtual string GetLogName() => Path.Combine(GetLogPath(), $@"{Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()?.Location)}_{DateTime.Now.ToString("dd")}.log");

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    protected LoggerBase(string logFilePath)
    {
        if (!string.IsNullOrEmpty(logFilePath))
            _logFilePath = logFilePath;
        else
        {
            // If null, then assemble the pathing for the user.
            try 
            {
                if (!Directory.Exists(GetLogPath()))
                    Directory.CreateDirectory(GetLogPath());

                _logFilePath = GetLogName();
            }
            catch (Exception) 
            {
                // On error, we'll attempt to determine the caller and use that as the log file's name.
                try
                {
                    _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"{Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()?.Location)}.log"); 
                }
                catch (Exception)
                {
                    _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"{Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location)}.log");
                }
            }
        }
    }

    #region [Props]
    /// <summary>
    /// Gets or sets the full path to the log file.
    /// </summary>
    public virtual string LogFilePath
    {
        get => _logFilePath;
        set => _logFilePath = value;
    }

    /// <summary>
    /// Gets or sets the full path to the log file.
    /// </summary>
    public virtual string TimeFormat
    {
        get => _timeFormat;
        set => _timeFormat = value;
    }

    /// <summary>
    /// Gets or sets the field delimiter.
    /// </summary>
    public virtual string Delimiter
    {
        get => _delimiter;
        set => _delimiter = value;
    }

    /// <summary>
    /// Gets or sets the file encoding.
    /// </summary>
    public virtual Encoding LogFileEncoding
    {
        get => _logFileEncoding;
        set => _logFileEncoding = value;
    }
    #endregion

    #region [Methods]
    /// <summary>
    /// Abstract method for logging a message, to be implemented by derived classes
    /// </summary>
    /// <param name="message">text to write</param>
    /// <param name="time">whether to include time-stamp preamble</param>
    public abstract void Write(string message, LogLevel level = LogLevel.INFO, bool time = true);

    /// <summary>
    /// Optionally, provide a virtual Dispose method that can be overridden by derived classes.
    /// </summary>
    public virtual void Dispose()
    {
        Console.WriteLine($"[INFO] {this.GetType()?.Name} of base type {this.GetType()?.BaseType?.Name} is disposing.");
    }

    /// <summary>
    /// Exception event for any listeners.
    /// </summary>
    /// <param name="ex"><see cref="Exception"/></param>
    public virtual void RaiseException(Exception ex) => OnException?.Invoke(ex);

    /// <summary>
    /// Provides a virtual method to determine if a file is being accessed by another thread.
    /// </summary>
    /// <param name="file"><see cref="FileInfo"/></param>
    /// <returns>true if file is in use, false otherwise</returns>
    public virtual bool IsFileLocked(FileInfo file)
    {
        FileStream? stream = null;
        try
        {
            if (!File.Exists(file.FullName))
                return false;

            stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) // still being written to or being accessed by another process 
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
        }
        return false; // file is not locked
    }
    #endregion
}
#endregion

#region [BufferedLogger]
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
/// <remarks>
/// This logger uses a <see cref="ConcurrentQueue{T}"/> to store the requests.
/// </remarks>
public class BufferedLogger : LoggerBase
{
    readonly ConcurrentQueue<string> _logQueue;
    readonly SemaphoreSlimEx _semaphore;
    readonly System.Threading.Timer _flushTimer;
    readonly int _flushInterval;
    bool _isDisposed = false;
    bool _writeInsideWhile = true;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    /// <param name="flushInterval">millisecond polling amount for write</param>
    public BufferedLogger(string logFilePath, int flushInterval = 4000) : base(logFilePath)
    {
        _logQueue = new ConcurrentQueue<string>();
        _semaphore = new SemaphoreSlimEx(1, 10);
        _flushInterval = flushInterval;
        _flushTimer = new System.Threading.Timer(async _ => await FlushLogBuffer(), null, _flushInterval, _flushInterval);
    }

    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
        {
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
        }
        else
        {
            try
            {
                _logQueue.Enqueue((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
                if (_logQueue.Count > (_minWait * 10))
                    Task.Run(() => FlushLogBuffer());
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
    }

    async Task FlushLogBuffer()
    {
        try
        {
            if (!_semaphore.IsDisposed)
                await _semaphore.WaitAsync().ConfigureAwait(false);

            if (_logQueue.IsEmpty)
                return;

            if (_writeInsideWhile)
            {
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                {
                    while (_logQueue.TryDequeue(out var logMessage))
                    {
                        await writer.WriteAsync($"{logMessage}\r\n");
                        //await writer.FlushAsync();
                    }
                }
            }
            else // stack messages into a single write
            {
                StringBuilder sb = new StringBuilder();
                while (_logQueue.TryDequeue(out var logMessage))
                {
                    sb.AppendLine(logMessage);
                }
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                {
                    await writer.WriteAsync(sb.ToString());
                    //await writer.FlushAsync();
                }
            }
        }
        catch (Exception ex)
        {
            RaiseException(ex);
        }
        finally
        {
            try
            {
                // https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.release
                // A call to the Release() method increments the CurrentCount property by one.
                // If the value of the CurrentCount property is zero before this method is called,
                // the method also allows one thread or task blocked by a call to the Wait or
                // WaitAsync method to enter the semaphore.
                if (!_semaphore.IsDisposed)
                    _semaphore?.Release();
            }
            catch (Exception) { }
        }
    }

    public override async void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _flushTimer.Dispose();

        await FlushLogBuffer();

        try
        {
            if (!_semaphore.IsDisposed)
                _semaphore.Dispose();
        }
        catch (Exception) { }

        base.Dispose();
    }
}
#endregion

#region [QueuedLogger]
/// <summary>
/// Creates a <see cref="System.Threading.Thread"/> that watches the 
/// <see cref="System.Collections.Concurrent.BlockingCollection{T}"/> 
/// for items to try and write to storage.
/// </summary>
public class QueuedLogger : LoggerBase
{
    bool _threadRunning = true;
    BlockingCollection<Message> _collection;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public QueuedLogger(string logFilePath) : base(logFilePath)
    {
        _collection = new BlockingCollection<Message>();
        // Configure our writing thread delegate.
        Thread thread = new Thread(() =>
        {
            while (_threadRunning)
            {
                // NOTE: To use an enumerable with the collection you would call GetConsumingEnumerable()
                //foreach (var item in _collection.GetConsumingEnumerable()) { Debug.WriteLine($"Consuming: {item}"); }

                if (_collection.Count > 0)
                    FlushCollection();
                else
                    Thread.Sleep(_minWait);
            }

            if (_collection.Count == 0)
            {
                _collection.CompleteAdding(); // do not accept more additions
                _collection.Dispose();
            }

            Debug.WriteLine($"[INFO] Leaving {Thread.CurrentThread.Name} thread");
        });
        // Set the priority and start it.
        thread.Priority = ThreadPriority.Lowest;
        thread.IsBackground = true;
        thread.Name = "QueuedLogger";
        thread.Start();
    }

    public override void Write(string value, LogLevel level, bool time = true)
    {
        if (level == LogLevel.OFF)
        {
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{value}");
        }
        else
        {
            try
            {
                if (!_collection.TryAdd(new Message(value, level, time)))
                {
                    RaiseException(new Exception($"Unable to add message to {nameof(BlockingCollection<Message>)}"));
                }
            }
            catch (InvalidOperationException)
            {
                RaiseException(new Exception($"Cannot add more to the {nameof(BlockingCollection<Message>)}"));
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
    }

    public override void Dispose()
    {
        try
        {
            // For cases where object was newed-up and immediately disposed (like the test).
            while (_collection.Count > 0)
                FlushCollection();
        }
        catch (Exception ex)
        {
            RaiseException(ex);
        }

        _threadRunning = false;
        base.Dispose();
    }

    void FlushCollection()
    {
        int maxTries = _minWait;
        Message msg;

        if (_collection.TryTake(out msg))
        {
            try
            {
                while (_threadRunning && IsFileLocked(new FileInfo(_logFilePath)) && --maxTries > 0)
                    Thread.Sleep(_minWait);

                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                {
                    writer.WriteLine((msg.time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{msg.level}{_delimiter}" : $"{msg.level}{_delimiter}") + $"{msg.text}");
                    //writer.Flush();
                }
            }
            catch (Exception ex) /* typically permission or file-lock issue */
            {
                RaiseException(ex);
            }
        }
        else
        {
            RaiseException(new Exception($"Unable to remove message from {nameof(BlockingCollection<Message>)}"));
        }
    }
}
#endregion

#region [TokenLogger]
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
/// <remarks>
/// This logger uses a <see cref="BlockingCollection{T}"/> to store the requests.
/// </remarks>
public class TokenLogger :  LoggerBase
{
    readonly BlockingCollection<string> _logQueue;
    readonly CancellationTokenSource _cts;
    readonly Task _processTask;
    bool _isDisposed = false;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public TokenLogger(string logFilePath) : base(logFilePath)
    {
        _logQueue = new BlockingCollection<string>();
        _cts = new CancellationTokenSource();
        _processTask = Task.Run(async () => await ProcessLogQueue(_cts.Token));
    }

    /// <summary>
    /// Enqueues a log message.
    /// </summary>
    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
        else
        {
            try
            {
                _logQueue?.TryAdd((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
            }
            catch (InvalidOperationException) 
            {
                RaiseException(new Exception($"Cannot add more to the {nameof(BlockingCollection<string>)}."));
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
    }

    async Task ProcessLogQueue(CancellationToken token)
    {
        do
        {
            try
            {
                foreach (var logMessage in _logQueue.GetConsumingEnumerable(token))
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                        {
                            await writer.WriteLineAsync(logMessage);
                            //await writer.FlushAsync();
                        }
                    }
                    catch (IOException)
                    {
                        try
                        {   // Try one more time before raising an exception event.
                            await Task.Delay(_minWait);
                            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                            {
                                await writer.WriteLineAsync(logMessage);
                                //await writer.FlushAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            RaiseException(ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        RaiseException(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                RaiseException(new Exception($"The {nameof(TokenLogger)} is being forced to stop."));
            }
            
            await Task.Delay(_minWait);

        } while (!_isDisposed);

        Debug.WriteLine($"[INFO] Leaving {Thread.CurrentThread.Name} thread");
    }

    public override void Dispose()
    {
        if (_isDisposed)
            return;

        // Stop accepting additions.
        _logQueue?.CompleteAdding();

        _isDisposed = true;

        //_cts.Cancel(); // moved, see below

        try
        {   // Wait for the processing task to finish
            _processTask?.Wait();
        }
        catch (AggregateException ex)
        {
            ex.Flatten().Handle(ex =>
            {
                Debug.WriteLine($"[WARNING] Type:{ex.GetType()}  Message:{ex.Message}");
                return true;
            });
        }

        // Moved this here so the loop can run minimum of once for immediate disposal scenarios.
        _cts.Cancel();
        _cts.Dispose();
        _logQueue?.Dispose();
        base.Dispose();
    }
}
#endregion

#region [DeferredLogger]
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
/// <remarks>
/// Concurrency is not guaranteed in the <see cref="DeferredLogger"/>, 
/// since each write will have it's own thread assigned. 
/// The <see cref="SemaphoreSlimEx"/> should help with the issue.
/// </remarks>
public class DeferredLogger : LoggerBase
{
    readonly SemaphoreSlimEx _semaphore;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public DeferredLogger(string logFilePath) : base(logFilePath)
    {
        _semaphore = new SemaphoreSlimEx(1, 10);
    }

    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
        else
            Task.Run(async () => await WriteToLogFileAsync((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}"));
    }

    async Task WriteToLogFileAsync(string message)
    {
        int maxTries = _minWait;

        if (!_semaphore.IsDisposed)
            await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            while (IsFileLocked(new FileInfo(_logFilePath)) && --maxTries > 0) { await Task.Delay(_minWait); }
            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
            {
                await writer.WriteLineAsync($"{message}");
                //await writer.FlushAsync();
            }
        }
        catch (Exception) /* typically permission or file-lock issue */
        {
            try
            {
                await Task.Delay(_minWait);
                // Try one more time before raising an exception event.
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                {
                    await writer.WriteLineAsync($"{message}");
                    //await writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        finally
        {
            try
            {   // https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.release
                // A call to the Release() method increments the CurrentCount property by one.
                // If the value of the CurrentCount property is zero before this method is called,
                // the method also allows one thread or task blocked by a call to the Wait or
                // WaitAsync method to enter the semaphore.
                if (!_semaphore.IsDisposed)
                    _semaphore?.Release();
            }
            catch (Exception) { }
        }
    }

    public override void Dispose()
    {
        if (!_semaphore.IsDisposed)
            _semaphore?.Dispose();

        base.Dispose();
    }
}
#endregion

#region [WaitHandleLogger]
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
/// <remarks>
/// This logger uses a <see cref="Queue{T}"/> to store the requests.
/// </remarks>
public class HandleLogger : LoggerBase
{
    EventWaitHandle ewhExit = new EventWaitHandle(false, EventResetMode.AutoReset);
    EventWaitHandle ewhReady = new EventWaitHandle(false, EventResetMode.AutoReset);
    Queue<string> logQueue = new Queue<string>();
    volatile int requestCounter = 0;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public HandleLogger(string logFilePath) : base(logFilePath)
    {
        StartMonitor();
    }

    void StartMonitor()
    {
        ThreadPool.QueueUserWorkItem((obj) =>
        {
            Thread.CurrentThread.Name = "HandleLogger";
            EventWaitHandle[] handles = new EventWaitHandle[] { ewhExit, ewhReady };
            while (EventWaitHandle.WaitAny(handles) != 0)
            {
                while (logQueue.Count > 0)
                {
                    lock (logQueue)
                    {
#if NET5_0_OR_GREATER
                        if (logQueue.TryDequeue(out string? msg))
                        {
                            WriteToFile($"{msg}");
                        }
#else
                        var msg = logQueue.Dequeue();
                        WriteToFile($"{msg}");
#endif
                    }
                }
            }
            Debug.WriteLine($"[INFO] Leaving {Thread.CurrentThread.Name} thread.");
            //handles = null;
        });
    }

    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
        else
        {
            lock (logQueue)
            {
                logQueue.Enqueue((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{message}");
                ewhReady.Set();
            }
        }
    }

    void WriteToFile(string message)
    {
        try
        {
            Interlocked.Increment(ref requestCounter);
            //Debug.WriteLine($"[INFO] Write request {requestCounter} on thread #{Thread.CurrentThread.ManagedThreadId}");

            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
            {
                writer.WriteLine($"{message}");
                //writer.Flush();
            }
        }
        catch (Exception) /* typically permission or file-lock issue */
        {
            try
            {
                Thread.Sleep(_minWait);
                // Try one more time before raising an exception event.
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                {
                    writer.WriteLine($"{message}");
                    //writer.Flush();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        finally
        {
            Interlocked.Decrement(ref requestCounter);
        }
    }

    public override void Dispose()
    {
        ewhExit.Set();

        // Flush any stragglers.
        while (logQueue.Count > 0)
        {
            string msg = string.Empty;
            lock (logQueue)
            {
#if NET5_0_OR_GREATER
                if (logQueue.TryDequeue(out msg))
                {
                    WriteToFile($"{msg}");
                }
#else
                if (logQueue.Count > 0)
                {
                    msg = logQueue.Dequeue();
                    WriteToFile($"{msg}");
                }
#endif
            }
        }

        base.Dispose();
    }
}
#endregion

#region [IntervalLogger]
/// <summary>
/// Creates a <see cref="System.Threading.Thread"/> that watches the 
/// <see cref="System.Collections.Concurrent.BlockingCollection{T}"/> 
/// for items to try and write to storage.
/// </summary>
public class IntervalLogger : LoggerBase
{
    bool _threadRunning = true;
    BlockingCollection<Message> _collection;
    DateTime _lastWrite;
    TimeSpan _writeInterval;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public IntervalLogger(string logFilePath, TimeSpan writeInterval) : base(logFilePath)
    {
        // Make sure we write at least once on creation.
        _lastWrite = DateTime.Now.Subtract(writeInterval);
        _writeInterval = writeInterval;
        _collection = new BlockingCollection<Message>();
        // Configure our writing thread delegate.
        Thread thread = new Thread(() =>
        {
            while (_threadRunning)
            {
                // NOTE: To use an enumerable with the collection you would call GetConsumingEnumerable()
                //foreach (var item in _collection.GetConsumingEnumerable()) { Debug.WriteLine($"Consuming: {item}"); }

                TimeSpan elapsed = DateTime.Now - _lastWrite;
                if (_collection.Count > 0 && IsAllowed())
                {
                    _lastWrite = DateTime.Now;
                    FlushCollection();
                }
                else
                    Thread.Sleep(_minWait);
            }

            if (_collection.Count == 0)
            {
                _collection.CompleteAdding(); // do not accept more additions
                _collection.Dispose();
            }

            Debug.WriteLine($"[INFO] Leaving {Thread.CurrentThread.Name} thread");
        });
        // Set the priority and start it.
        thread.Priority = ThreadPriority.Lowest;
        thread.IsBackground = true;
        thread.Name = "IntervalLogger";
        thread.Start();
    }

    /// <summary>
    /// Gets or sets the writing interval.
    /// </summary>
    public TimeSpan WriteInterval
    {
        get => _writeInterval;
        set => _writeInterval = value;
    }

    public bool IsAllowed()
    {
        TimeSpan elapsed = DateTime.Now - _lastWrite;
        return elapsed >= _writeInterval;
    }

    public override void Write(string value, LogLevel level, bool time = true)
    {
        if (level == LogLevel.OFF)
        {
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{value}");
        }
        else
        {
            try
            {
                if (!_collection.TryAdd(new Message(value, level, time)))
                {
                    RaiseException(new Exception($"Unable to add message to {nameof(BlockingCollection<Message>)}"));
                }
            }
            catch (InvalidOperationException)
            {
                RaiseException(new Exception($"Cannot add more to the {nameof(BlockingCollection<Message>)}"));
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
    }

    public override void Dispose()
    {
        try
        {   // For cases where object was newed-up and immediately disposed.
            if (_collection.Count > 0)
                FlushCollection();
        }
        catch (Exception ex)
        {
            RaiseException(ex);
        }

        _threadRunning = false;
        base.Dispose();
    }

    void FlushCollection()
    {
        while (_collection.Count > 0)
        {
            Message msg;
            try
            {
                if (_collection.TryTake(out msg))
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                        {
                            writer.WriteLine((msg.time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{msg.level}{_delimiter}" : $"{msg.level}{_delimiter}") + $"{msg.text}");
                            //writer.Flush();
                        }
                    }
                    catch (Exception) /* typically permission or file-lock issue */
                    {
                        try
                        {
                            // Try one more time before raising an exception event.
                            Thread.Sleep(_minWait);
                            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                            {
                                writer.WriteLine((msg.time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{msg.level}{_delimiter}" : $"{msg.level}{_delimiter}") + $"{msg.text}");
                                //writer.Flush();
                            }
                        }
                        catch (Exception ex)
                        {
                            RaiseException(ex);
                        }
                    }
                }
                else
                {
                    RaiseException(new Exception($"Unable to remove message from {nameof(BlockingCollection<Message>)}"));
                }
            }
            catch (Exception ex) /* blocking collection error */
            {
                RaiseException(ex);
            }
        }
    }
}
#endregion

#region [HashSetLogger]
/// <summary>
/// Creates a <see cref="System.Threading.Thread"/> that watches the 
/// <see cref="System.Collections.Generic.HashSet{T}"/>  for items to 
/// add to the <see cref="BlockingCollection{T}"/> and then write.
/// </summary>
public class HashSetLogger : LoggerBase
{
    bool _working = false;
    bool _threadRunning = true;
    double _daysUntilWipe = 1;
    DateTime _lastClear;
    HashSet<Message> _collection;
    BlockingCollection<string> _output;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public HashSetLogger(string logFilePath) : base(logFilePath)
    {
        _lastClear = DateTime.Now;
        _collection = new HashSet<Message>();
        _output = new BlockingCollection<string>();

        // Configure our set monitoring delegate.
        Thread thread = new Thread(() =>
        {
            while (_threadRunning)
            {
                Thread.Sleep(_minWait);

                // By default, we clear the buffer once per day.
                if ((DateTime.Now - _lastClear).TotalDays >= _daysUntilWipe && !_working)
                {
                    _lastClear = DateTime.Now;
                    lock (_collection)
                    {
                        _collection.Clear();
                    }
                }

                if (_output.Count > 0)
                    FlushOutput();

            }
            Debug.WriteLine($"[INFO] Leaving {Thread.CurrentThread.Name} thread");
            _collection.Clear();
        });
        // Set the priority and start it.
        thread.Priority = ThreadPriority.Lowest;
        thread.IsBackground = true;
        thread.Name = "HashSetLogger";
        thread.Start();
    }

    /// <summary>
    /// Gets or sets how many days elapse before flushing the hash set.
    /// </summary>
    /// <remarks>The default is one day.</remarks>
    public double DaysUntilWipe
    {
        get => _daysUntilWipe;
        set => _daysUntilWipe = value;
    }

    public override void Write(string value, LogLevel level, bool time = true)
    {
        if (level == LogLevel.OFF)
        {
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{level}{_delimiter}" : $"{level}{_delimiter}") + $"{value}");
        }
        else
        {
            try
            {
                _working = true;
                lock (_collection)
                {
                    var msg = new Message(value, level, time);
                    if (!_collection.Add(msg))
                    {
                        Debug.WriteLine($"[INFO] This message already exists in the collection and will be skipped.");
                    }
                    else
                    {
                        _output.Add((msg.time ? $"{DateTime.Now.ToString(_timeFormat)}{_delimiter}{msg.level}{_delimiter}" : $"{msg.level}{_delimiter}") + $"{msg.text}");
                    }
                }
            }
            catch (Exception) { }
            finally { _working = false; }
        }
    }

    void FlushOutput()
    {
        while (_output.Count > 0)
        {
            try
            {
                string? msg;
                if (_output.TryTake(out msg))
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                        {
                            writer.WriteLine($"{msg}");
                        }
                    }
                    catch (Exception) /* typically permission or file-lock issue */
                    {
                        try
                        {
                            // Try one more time before raising an exception event.
                            Thread.Sleep(_minWait);
                            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, _logFileEncoding))
                            {
                                writer.WriteLine($"{msg}");
                            }
                        }
                        catch (Exception ex)
                        {
                            RaiseException(ex);
                        }
                    }
                }
                else
                {
                    RaiseException(new Exception($"Unable to remove message from {nameof(BlockingCollection<string>)}"));
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
    }

    public override void Dispose()
    {
        try
        {   // For cases where object was newed-up and immediately disposed.
            if (_collection.Count > 0)
                FlushOutput();
        }
        catch (Exception ex)
        {
            RaiseException(ex);
        }
        _threadRunning = false;
        base.Dispose();
    }
}
#endregion
