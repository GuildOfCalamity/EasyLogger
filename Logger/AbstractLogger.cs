using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Logger;

public enum LogLevel
{
    OFF = 0,
    INFO = 1 << 0,    // 2^0 (1)
    WARNING = 1 << 1, // 2^1 (2)
    ERROR = 1 << 2,   // 2^3 (4)
    FATAL = 1 << 3,   // 2^4 (8)
}

// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
public abstract class LoggerBase : IDisposable
{
    public event Action<Exception> OnException;
    protected readonly string _logFilePath;
    protected readonly string _delim ="\t";
    protected readonly int _minWait = 5; // milliseconds
    protected readonly string _timeFormat = "yyyy-MM-dd hh:mm:ss.fff tt"; // 2024-08-24 11:30:00.000 AM

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    protected LoggerBase(string logFilePath)
    {
        if (!string.IsNullOrEmpty(logFilePath))
            _logFilePath = logFilePath;
        else
            _logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.log");
    }

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
    /// Gets the full path to the log file.
    /// </summary>
    public virtual string LogFilePath() => _logFilePath;

    /// <summary>
    /// Provides a virtual method to determine if a file is being accessed by another thread.
    /// </summary>
    /// <param name="file"><see cref="FileInfo"/></param>
    /// <returns>true if file is in use, false otherwise</returns>
    public virtual bool IsFileLocked(FileInfo file)
    {
        FileStream stream = null;
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
}

// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
public class DeferredLogger : LoggerBase
{
    private readonly SemaphoreSlimEx _semaphore;

    /// <summary>
    /// Base constructor
    /// </summary>
    /// <param name="logFilePath">full path to log file</param>
    public DeferredLogger(string logFilePath) : base(logFilePath)
    {
        _semaphore = new SemaphoreSlimEx(1, 1);
    }

    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {level}{_delim} " : $"{level}{_delim} ") + $"{message}");
        else
            Task.Run(async () => await WriteLogToFileAsync((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {level}{_delim} " : $"{level}{_delim} ") + $"{message}"));
    }

    async Task WriteLogToFileAsync(string message)
    {
        int maxTries = _minWait * 2;

        if (!_semaphore.IsDisposed)
            await _semaphore?.WaitAsync();

        try
        {
            while (IsFileLocked(new FileInfo(_logFilePath)) && --maxTries > 0) { await Task.Delay(_minWait); }
            using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
            {
                await writer.WriteLineAsync($"{message}");
                //await writer.FlushAsync();
            }
        }
        catch (Exception)
        {
            try
            {   // Try one more time before raising an exception event.
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
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
            {
                // https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.release?view=netframework-4.8.1
                // A call to the Release() method increments the CurrentCount property by one.
                // If the value of the CurrentCount property is zero before this method is called,
                // the method also allows one thread or task blocked by a call to the Wait or
                // WaitAsync method to enter the semaphore.
                if (!_semaphore.IsDisposed && _semaphore.CurrentCount < 1)
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

// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
/// <summary>
/// Simple deferred-style logger for non-blocked calls with file I/O.
/// </summary>
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
        _semaphore = new SemaphoreSlimEx(1, 1);
        _flushInterval = flushInterval;
        _flushTimer = new System.Threading.Timer(async _ => await FlushLogBuffer(), null, _flushInterval, _flushInterval);
    }

    public override void Write(string message, LogLevel level, bool time)
    {
        if (level == LogLevel.OFF)
        {
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {level}{_delim} " : $"{level}{_delim} ") + $"{message}");
        }
        else
        {
            _logQueue.Enqueue((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {level}{_delim} " : $"{level}{_delim} ") + $"{message}");
            if (_logQueue.Count > (_minWait * 10))
                Task.Run(() => FlushLogBuffer());
        }
    }

    async Task FlushLogBuffer()
    {
        if (!_semaphore.IsDisposed)
            await _semaphore?.WaitAsync();

        try
        {
            if (_logQueue.IsEmpty)
                return;

            if (_writeInsideWhile)
            {
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
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
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
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
                // https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.release?view=netframework-4.8.1
                // A call to the Release() method increments the CurrentCount property by one.
                // If the value of the CurrentCount property is zero before this method is called,
                // the method also allows one thread or task blocked by a call to the Wait or
                // WaitAsync method to enter the semaphore.
                if (!_semaphore.IsDisposed && _semaphore.CurrentCount < 1)
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
        //int maxTries = _minWait * 2;
        //var awaiter = FlushLogBuffer().GetAwaiter();
        //while (!awaiter.IsCompleted && --maxTries > 0)
        //    await Task.Delay(_minWait);

        if (!_semaphore.IsDisposed)
            _semaphore.Dispose();

        base.Dispose();
    }
}

// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
/// <summary>
/// Creates a <see cref="System.Threading.Thread"/> that watches the 
/// <see cref="System.Collections.Concurrent.BlockingCollection{T}"/> 
/// for items to try and write to storage.
/// </summary>
public class QueuedLogger : LoggerBase
{
    bool _threadRunning = true;
    BlockingCollection<Message> _collection;

    public QueuedLogger(string logFilePath) : base(logFilePath)
    {
        _collection = new BlockingCollection<Message>();
        // Configure our writing thread delegate.
        Thread thread = new Thread(() =>
        {
            while (_threadRunning)
            {
                // NOTE: To use an enumerable with the collection you would call GetConsumingEnumerable()
                //foreach (var item in _collection.GetConsumingEnumerable()) { Console.WriteLine($"Consuming: {item}"); }

                if (_collection.Count > 0)
                    FlushCollection();
                else
                    Thread.Sleep(_minWait);
            }

            if (_collection.Count == 0)
                _collection.Dispose();
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
            Console.WriteLine((time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {level}{_delim} " : $"{level}{_delim} ") + $"{value}");
        }
        else
        {
            if (!_collection.TryAdd(new Message(value, level, time)))
            {
                RaiseException(new Exception("Unable to add message to BlockingCollection"));
            }
            //m_Collection.CompleteAdding(); //marks the collection as not accepting any more additions
        }
    }

    public override void Dispose()
    {
        try
        {
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
        int maxTries = _minWait * 2;
        Message msg;

        if (_collection.TryTake(out msg))
        {
            try
            {
                while (_threadRunning && IsFileLocked(new FileInfo(_logFilePath)) && --maxTries > 0)
                    Thread.Sleep(_minWait);

                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8))
                {
                    writer.WriteLine((msg.time ? $"{DateTime.Now.ToString(_timeFormat)}{_delim} {msg.level}{_delim} " : $"{msg.level}{_delim} ") + $"{msg.text}");
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
            RaiseException(new Exception("Unable to remove message from BlockingCollection!"));
        }
    }

    #region [Message structure]
    struct Message
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
}

// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
// -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
/// <summary>
/// Basic test class.
/// </summary>
public static class TestLogger
{
    public static void Run()
    {
        Console.WriteLine($"{Environment.NewLine}• Testing Deferred {nameof(LoggerBase)}…");
        using (LoggerBase log = new DeferredLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerDeferred.txt")))
        {
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** something extra could go here **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");
            
            Console.WriteLine($"{log.LogFilePath()}");
        }

        Console.WriteLine($"{Environment.NewLine}• Testing Buffered {nameof(LoggerBase)}…");
        using (LoggerBase log = new BufferedLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerBuffered.txt")))
        {
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** something extra could go here **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath()}");
        }

        Console.WriteLine($"{Environment.NewLine}• Testing Queued {nameof(LoggerBase)}…");
        using (LoggerBase log = new QueuedLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerQueued.txt")))
        {
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** something extra could go here **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath()}");
        }
    }
}