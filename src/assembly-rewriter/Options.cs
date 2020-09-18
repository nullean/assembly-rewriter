using System.Collections.Generic;
using CommandLine;

namespace AssemblyRewriter
{
	public class Options
	{
		[Option('i', "in", Min = 1, Required = true, HelpText = "input path for assembly to rewrite. Use multiple flags for multiple input paths")]
		public IEnumerable<string> InputPaths { get; set; }

		[Option('o', "out", Min = 1, Required = true, HelpText = "output path for rewritten assembly. Use multiple flags for multiple output paths")]
		public IEnumerable<string> OutputPaths { get; set; }

		[Option('r', "resolvedir", HelpText = "Additional assembly resolve directories. Use multiple flags for multiple resolve directories")]
		public IEnumerable<string> ResolveDirectories { get; set; }

		[Option('k', "keyfile", HelpText = "Sign rewritten assembly with this key file. When merge option is specified, the merged assembly will be signed.")]
		public string KeyFile { get; set; }

		[Option('m', "merge", Default = false, HelpText = "Merge all rewritten assemblies into a single assembly using the first output path as target")]
		public bool Merge { get; set; }

		[Option('v', "verbose", Default = false, HelpText = "verbose output")]
		public bool Verbose { get; set; }
	}
}
