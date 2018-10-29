using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace AssemblyRewriter
{
    internal class AssemblyResolver : DefaultAssemblyResolver
    {
        private readonly IEnumerable<string> _directories;

        public AssemblyResolver(IEnumerable<string> directories) =>
            _directories = new HashSet<string>(directories.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            try
            {
                return base.Resolve(name);
            }
            catch
            {
                foreach (var directory in _directories)
                {
                    var filePath = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(filePath))
                        return AssemblyDefinition.ReadAssembly(filePath);
                }

                throw;
            }
        }
    }
}
