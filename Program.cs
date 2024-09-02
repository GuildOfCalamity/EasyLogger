using System;
using System.Text;
using Logger;

namespace EasyLogger;

public class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        TestLogger.Run();

        Console.Write($"{Environment.NewLine}• Press any key to exit…");

        _ = Console.ReadKey(true);
    }
}
