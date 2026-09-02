using System.ComponentModel.DataAnnotations;
using ILRepacking;
using Nullean.Argh;

namespace AssemblyRewriter;

internal sealed class AssemblyRewriterCommands
{
	/// <summary>Rewrites assemblies and namespaces.</summary>
	/// <param name="input">-i, --in, Input path for assembly to rewrite. Use multiple flags for multiple input paths.</param>
	/// <param name="output">-o, --out, Output path for rewritten assembly. Use multiple flags for multiple output paths.</param>
	/// <param name="resolveDir">-r, --resolvedir, Additional assembly resolve directories. Use multiple flags for multiple resolve directories.</param>
	/// <param name="keyFile">-k, --keyfile, Sign rewritten assembly with this key file. When merge option is specified, the merged assembly will be signed.</param>
	/// <param name="merge">-m, --merge, Merge all rewritten assemblies into a single assembly using the first output path as target.</param>
	/// <param name="verbose">-v, --verbose, Verbose output.</param>
	[DefaultCommand]
	public int Rewrite(
		[MinLength(1)] List<string> input,
		[MinLength(1)] List<string> output,
		List<string>? resolveDir = null,
		string? keyFile = null,
		bool merge = false,
		bool verbose = false)
	{
		if (input.Count != output.Count)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Number of input paths must equal number of output paths");
			Console.ResetColor();
			return 1;
		}

		var options = new Options
		{
			InputPaths = input,
			OutputPaths = output,
			ResolveDirectories = resolveDir ?? [],
			KeyFile = keyFile,
			Merge = merge,
			Verbose = verbose
		};

		try
		{
			var rewriter = new AssemblyRewriter(options);
			rewriter.Rewrite(options.InputPaths, options.OutputPaths, options.ResolveDirectories);
		}
		catch (Exception e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(e);
			Console.ResetColor();
			return 1;
		}

		if (!merge) return 0;

		try
		{
			var repackOptions = new RepackOptions
			{
				Internalize = true,
				Closed = true,
				KeepOtherVersionReferences = false,
				TargetKind = ILRepack.Kind.SameAsPrimaryAssembly,
				InputAssemblies = output.ToArray(),
				LineIndexation = true,
				OutputFile = output.First(),
				KeyFile = keyFile,
				SearchDirectories = output.Select(p => new DirectoryInfo(p).FullName).Distinct(),
			};

			var pack = new ILRepack(repackOptions, new RepackConsoleLogger());
			pack.Repack();
		}
		catch (Exception e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(e);
			Console.ResetColor();
			return 2;
		}

		return 0;
	}
}
