using System.IO;

namespace AssemblyRewriter
{
	internal class AssemblyToRewrite
	{
		private string _inputDirectory;
		private string _inputName;
		private string _outputDirectory;
		private string _outputName;

		public AssemblyToRewrite(string inputPath, string outputPath)
		{
			InputPath = Path.GetFullPath(inputPath);
			OutputPath = Path.GetFullPath(outputPath);
		}

		public string InputPath { get; }

		public string InputDirectory => _inputDirectory ??= Path.GetDirectoryName(InputPath);

		public string InputName => _inputName ??= Path.GetFileNameWithoutExtension(InputPath);

		public string OutputDirectory => _outputDirectory ??= Path.GetDirectoryName(OutputPath);

		public string OutputName => _outputName ??= Path.GetFileNameWithoutExtension(OutputPath);

		public string OutputPath { get; }

		public bool Rewritten { get; set; }
	}
}
