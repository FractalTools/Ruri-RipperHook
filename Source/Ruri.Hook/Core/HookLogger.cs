using System;

namespace Ruri.Hook.Core
{
    public static class HookLogger
    {
        private const string Prefix = "[RuriHook] ";

        public static void Log(string message)
        {
            Console.WriteLine($"{Prefix}{message}");
        }

        public static void LogSuccess(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{Prefix}{message}");
            Console.ForegroundColor = oldColor;
        }

        public static void LogFailure(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{Prefix}{message}");
            Console.ForegroundColor = oldColor;
        }

        public static void LogWarning(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{Prefix}{message}");
            Console.ForegroundColor = oldColor;
        }
    }
}
