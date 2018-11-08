using System;
using ILRepacking;

namespace AssemblyRewriter
{
    internal class RepackConsoleLogger : ILogger
    {
        private void Write(string level, string msg)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.ffzzz}][");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"Repack");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"][");
            Console.ForegroundColor = this.LevelToConsoleColor(level);
            Console.Write($"{level.PadRight(7)}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"]");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        private ConsoleColor LevelToConsoleColor(string level)
        {
            switch (level)
            {
                case nameof(this.Error): return ConsoleColor.Red;
                case nameof(this.Warn): return ConsoleColor.Yellow;
                case nameof(this.Info): return ConsoleColor.Blue;
                case nameof(this.Verbose): return ConsoleColor.Gray;
            }
            return ConsoleColor.Gray;
        }

        public void Log(object str) => this.Write(nameof(this.Log), str.ToString());

        public void Error(string msg) => this.Write(nameof(this.Error), msg);

        public void Warn(string msg) => this.Write(nameof(this.Warn), msg);

        public void Info(string msg) =>this.Write(nameof(this.Info), msg);

        public void Verbose(string msg)
        {
            if (!ShouldLogVerbose) return;
            this.Write(nameof(this.Verbose), msg);
        }

        public void DuplicateIgnored(string ignoredType, object ignoredObject) =>
            this.Write(nameof(this.Warn), $"ignoredType:{ignoredType} ignoredObject:{ignoredObject}");

        public bool ShouldLogVerbose { get; set; }
    }
}
