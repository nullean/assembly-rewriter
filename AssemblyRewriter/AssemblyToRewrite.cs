using System.IO;

namespace AssemblyRewriter
{
    class AssemblyToRewrite
    {
        private string _inputName;
        private string _outputName;
        private string _inputDirectory;

        public AssemblyToRewrite(string inputPath, string outputPath)
        {
            InputPath = Path.GetFullPath(inputPath);
            OutputPath = Path.GetFullPath(outputPath);
        }
        
        public string InputPath { get; }

        public string InputDirectory => _inputDirectory ?? (_inputDirectory = Path.GetDirectoryName(InputPath));

        public string InputName => _inputName ?? (_inputName = Path.GetFileNameWithoutExtension(InputPath));

        public string OutputName => _outputName ?? (_outputName = Path.GetFileNameWithoutExtension(OutputPath));
        
        public string OutputPath { get; }
        
        public bool Rewritten { get; set; }
    }
}