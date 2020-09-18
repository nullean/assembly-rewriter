using System;
using System.IO;
using System.Linq;
using CommandLine;
using CommandLine.Text;
using ILRepacking;

namespace AssemblyRewriter
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			using var parser = new Parser(settings =>
			{
				settings.HelpWriter = null;
				settings.IgnoreUnknownArguments = false;
			});

			var result = parser.ParseArguments<Options>(args);

			return result switch
			{
				Parsed<Options> parsed => Run(parsed.Value),
				NotParsed<Options> notParsed => HandleError(notParsed),
				_ => 1
			};
		}

		private static int Run(Options options)
		{
			if (options.InputPaths.Count() != options.OutputPaths.Count())
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Number of input paths must equal number of output paths");
				Console.ResetColor();
				return 1;
			}

			try
			{
				var rewriter = new AssemblyRewriter(options);
				rewriter.Rewrite(options.InputPaths, options.OutputPaths, options.ResolveDirectories);
			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine(e);
				return 1;
			}
			if (!options.Merge) return 0;
			try
			{
				var repackOptions = new RepackOptions
				{
					Internalize = true,
					Closed = true,
					KeepOtherVersionReferences = false,
					TargetKind = ILRepack.Kind.SameAsPrimaryAssembly,
					InputAssemblies = options.OutputPaths.ToArray(),
					LineIndexation = true,
					OutputFile = options.OutputPaths.First(),
					KeyFile = options.KeyFile,
					SearchDirectories = options.OutputPaths.Select(p=> new DirectoryInfo(p).FullName).Distinct(),
				};

				var pack = new ILRepack(repackOptions, new RepackConsoleLogger());
				pack.Repack();
			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine(e);
				return 2;
			}
			return 0;
		}

		private static int HandleError(NotParsed<Options> notParsed)
		{
			var helpText = HelpText.AutoBuild(notParsed, h =>
			{
				h.AdditionalNewLineAfterOption = false;
				h.Heading = "AssemblyRewriter" +
				            Environment.NewLine +
				            "----------------" +
				            Environment.NewLine +
				            "Rewrites assemblies and namespaces";
				h.AddPostOptionsLine("Each input path must have a corresponding output path");
				return HelpText.DefaultParsingErrorsHandler(notParsed, h);
			}, e => e);

			if (notParsed.Errors.IsHelp() || notParsed.Errors.IsVersion())
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine(helpText);
				Console.ResetColor();
				return 0;
			}

			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(helpText);
			Console.ResetColor();
			return 1;
		}
	}
}
