using System;

namespace EasyLogger
{
    /// <summary>
    /// Utility class to assist with performance timing.
    /// </summary>
    public class StopClock : IDisposable
    {
        private System.Diagnostics.Stopwatch m_watch;
        private string m_title;
        private bool m_console;
        private ConsoleColor m_color;

        public StopClock(string title = "", ConsoleColor color = ConsoleColor.Green, bool console = true)
        {
            m_watch = System.Diagnostics.Stopwatch.StartNew();
            m_title = title;
            m_console = console;
            m_color = color;
        }

        public System.Diagnostics.Stopwatch Stop()
        {
            if (m_watch != null)
                m_watch.Stop();

            return m_watch;
        }

        public void Print()
        {
            if (m_watch != null)
            {
                if (m_console)
                {
                    Console.ForegroundColor = m_color;
                    double result = (double)m_watch.ElapsedMilliseconds / 1000.0;
                    if (Console.CursorLeft > 0) { Console.WriteLine(); } // if there's already data on the line then add a CRLF
                    Console.WriteLine($"• {(string.IsNullOrEmpty(m_title) ? "" : $"{m_title}: ")}Execution lasted {m_watch.ElapsedMilliseconds} ms ({result.ToString("0.0")} sec)");
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                else
                {
                    double result = (double)m_watch.ElapsedMilliseconds / 1000.0;
                    System.Diagnostics.Debug.WriteLine(new string('=', 70));
                    System.Diagnostics.Debug.WriteLine($"[INFO] {(string.IsNullOrEmpty(m_title) ? "" : $"{m_title}: ")}Execution lasted {m_watch.ElapsedMilliseconds} ms ({result.ToString("0.0")} sec)");
                    System.Diagnostics.Debug.WriteLine(new string('=', 70));
                }
            }
        }

        public void Dispose()
        {
            Stop();
            Print();
        }

        /// <summary>
        /// Finalizer for safety (if the Dispose method isn't explicitly called)
        /// </summary>
        ~StopClock() => Dispose();
    }

}
