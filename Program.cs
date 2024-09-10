using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Logger;

namespace EasyLogger;

public class Program
{
    static void Main(string[] args)
    {
        #region [Init]
        Console.OutputEncoding = Encoding.UTF8;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        Console.CancelKeyPress += new ConsoleCancelEventHandler(ConsoleHandler);
        ListReferencedAssemblies();
        #endregion

        TestLoggers.Run();

        PressKeyTo("EXIT", ConsoleKey.Escape);
    }

    #region [Extras]
    static ConsoleKey _lastKey;
    public static Action<string, ConsoleKey> PressKeyTo = (str, key) =>
    {
        Console.Write($"{Environment.NewLine}• Press {key} key to {str} ");
        _lastKey = Console.ReadKey(true).Key;
        while (key != _lastKey)
        {
            Console.Write($"{Environment.NewLine}• Incorrect key: {_lastKey}, please try again. ");
            _lastKey = Console.ReadKey(true).Key;
        }
        Console.Write($"{Environment.NewLine}• Closing…");
        new System.Threading.AutoResetEvent(false).WaitOne(1000);
        Environment.Exit(0);
    };

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Console.WriteLine($"• UnhandledException: {(e.ExceptionObject as Exception)?.Message}");
        Console.WriteLine($"• StackTrace: {Environment.StackTrace}");
    }

    static void ConsoleHandler(object? sender, ConsoleCancelEventArgs args)
    {
        Console.WriteLine();
        Console.WriteLine($"• Key pressed......: {args.SpecialKey}");
        Console.WriteLine($"• Cancel property..: {args.Cancel}");
        args.Cancel = false; // Set the Cancel property to true to prevent terminating.
        Console.CursorVisible = true;
        Environment.Exit(1);
    }

    static void ListReferencedAssemblies()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        AssemblyName main = assembly.GetName();
        Console.WriteLine($"Main Assembly: {main.Name}, Version: {main.Version}");
        foreach (var sas in assembly.GetReferencedAssemblies().OrderBy(o => o.Name))
        {
            Console.WriteLine($" Sub Assembly: {sas.Name}, Version: {sas.Version}");
        }
    }
    #endregion

}

#region [Testing]
/// <summary>
/// Basic test class.
/// </summary>
public static class TestLoggers
{
    public static void Run()
    {
        #region [BufferedLogger]
        Console.WriteLine($"{Environment.NewLine}• Testing Buffered {nameof(LoggerBase)}…");
        using (LoggerBase log = new BufferedLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerBuffered.txt")))
        {
            log.TimeFormat = "MM-dd-yyyy hh:mm:ss.fff tt";
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** 
                something extra could go here 
            **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [QueuedLogger]
        Console.WriteLine($"{Environment.NewLine}• Testing Queued {nameof(LoggerBase)}…");
        using (LoggerBase log = new QueuedLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerQueued.txt")))
        {
            log.TimeFormat = "MM/dd/yyyy hh:mm:ss.fff tt";
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** 
                something extra could go here 
            **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [TokenLogger]
        Console.WriteLine($"{Environment.NewLine}• Testing Token {nameof(LoggerBase)}…");
        using (LoggerBase log = new TokenLogger("")) // Let the logger decide on the file name.
        {
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            /** 
                something extra could go here 
            **/
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [WaitHandleLogger]
        // -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
        Console.WriteLine($"{Environment.NewLine}• Testing WaitHandle {nameof(LoggerBase)}…");
        using (LoggerBase log = new HandleLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerHandle.txt")))
        {
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");

            for (int i = 1; i < 21; i++)
            {
                log.Write($"Index #{i}");
            }

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [DeferredLogger]
        Console.WriteLine($"{Environment.NewLine}• Testing Deferred {nameof(LoggerBase)}…");
        using (LoggerBase log = new DeferredLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerDeferred.txt")))
        {
            log.TimeFormat = "yyyy-MM-dd hh:mm:ss.fff tt";
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            for (int i = 1; i < 6; i++) 
            { 
                Thread.Sleep(10); 
                log.Write($"Index #{i}"); 
            }

            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");

            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [HashSetLogger]
        Console.WriteLine($"{Environment.NewLine}• Testing HashSet {nameof(LoggerBase)}…");
        using (LoggerBase log = new HashSetLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerHashSet.txt")))
        {
            ((HashSetLogger)log).DaysUntilWipe = 0.5; // Example of changing the flush trigger.
            log.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };
            Task.Run(() =>
            {
                for (int i = 1; i < 101; i++)
                {
                    log.Write($"This should only appear once in the log file.");
                }
            });
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test started.");
            for (int i = 1; i < 101; i++)
            {
                log.Write($"This should only appear once in the log file.");
            }
            log.Write($"{log.GetType()?.Name} of base type {log.GetType()?.BaseType?.Name} - Test finished.");
            Console.WriteLine($"{log.LogFilePath}");
        }
        #endregion

        #region [IntervalLogger]
        // This test is different since we want to keep the object around for some time to test interval writing.
        Console.WriteLine($"{Environment.NewLine}• Testing Interval {nameof(LoggerBase)}…");
        LoggerBase iLog = new IntervalLogger(Path.Combine(Directory.GetCurrentDirectory(), $"LoggerInterval.txt"), TimeSpan.FromSeconds(10));
        iLog.OnException += (ex) => { Debug.WriteLine($"[WARNING] {ex.Message}"); };
        iLog.LogFileEncoding = Encoding.ASCII; // Example of changing file encoding after object creation.
        ((IntervalLogger)iLog).WriteInterval = TimeSpan.FromSeconds(2); // Example of changing time interval after object creation.
        iLog.Write($"{iLog.GetType()?.Name} of base type {iLog.GetType()?.BaseType?.Name} - Test started.");
        for (int i = 1; i < 6; i++)
        {
            Thread.Sleep(10);
            // There is no need to call IsAllowed() from the user's point of view, for the test it serves as an awaiter.
            while (!((IntervalLogger)iLog).IsAllowed())
            {
                Console.Write($"•");
                Thread.Sleep(333);
            }
            iLog.Write($"Index #{i}");
        }
        Console.WriteLine();
        iLog.Write($"{iLog.GetType()?.Name} of base type {iLog.GetType()?.BaseType?.Name} - Test finished.");
        Console.WriteLine($"{iLog.LogFilePath}");
        iLog.Dispose();
        #endregion
    }
}
#endregion
