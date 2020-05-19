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
			Console.ForegroundColor = LevelToConsoleColor(level);
			Console.Write($"{level.PadRight(7)}");
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.Write($"]");
			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine(msg);
			Console.ResetColor();
		}

		private ConsoleColor LevelToConsoleColor(string level) =>
			level switch
			{
				nameof(Error) => ConsoleColor.Red,
				nameof(Warn) => ConsoleColor.Yellow,
				nameof(Info) => ConsoleColor.Blue,
				nameof(Verbose) => ConsoleColor.Gray,
				_ => ConsoleColor.Gray
			};

		public void Log(object str) => Write(nameof(Log), str.ToString());

		public void Error(string msg) => Write(nameof(Error), msg);

		public void Warn(string msg) => Write(nameof(Warn), msg);

		public void Info(string msg) =>Write(nameof(Info), msg);

		public void Verbose(string msg)
		{
			if (!ShouldLogVerbose) return;
			Write(nameof(Verbose), msg);
		}

		public void DuplicateIgnored(string ignoredType, object ignoredObject) =>
			Write(nameof(Warn), $"ignoredType:{ignoredType} ignoredObject:{ignoredObject}");

		public bool ShouldLogVerbose { get; set; }
	}
}
