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
    }
}
