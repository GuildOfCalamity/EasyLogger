using System;
using System.Collections.Concurrent;
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
        #endregion

        TestLogger.Run();

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
    #endregion
}
