using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Commands
{
    public class MSBuildPackTargetArgs
    {
        public string TargetPath { get; set; }
        public string AssemblyName { get; set; }
        public string OutputPath { get; set; }
        public string Configuration { get; set; }
        public IEnumerable<ProjectToProjectReference>  ProjectReferences { get; set; }
        public Dictionary<string, HashSet<string>> ContentFiles { get; set; } 

        public MSBuildPackTargetArgs()
        {
            ProjectReferences = new List<ProjectToProjectReference>();
        }
    }

    public struct ProjectToProjectReference
    {
        public string AssemblyName { get; set; }
        public string TargetPath { get; set; }
    }
}
