using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Logger;

namespace EasyLogger
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

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

            Console.Write($"{Environment.NewLine}• Press any key to exit…");
            _ = Console.ReadKey(true);
        }
    }
}
