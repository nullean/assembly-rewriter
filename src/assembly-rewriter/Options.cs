using System.Collections.Generic;

namespace AssemblyRewriter
{
	public class Options
	{
		public IEnumerable<string> InputPaths { get; set; }

		public IEnumerable<string> OutputPaths { get; set; }

		public IEnumerable<string> ResolveDirectories { get; set; } = [];

		public string KeyFile { get; set; }

		public bool Merge { get; set; }

		public bool Verbose { get; set; }
	}
}
