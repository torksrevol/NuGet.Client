using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NuGet.Commands
{
    public class MSBuildProjectFactory : IProjectFactory
    {
        private Common.ILogger _logger;
        
        // Files we want to always exclude from the resulting package
        private static readonly HashSet<string> _excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            NuGetConstants.PackageReferenceFile,
            "Web.Debug.config",
            "Web.Release.config"
        };

        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Packaging folders
        private const string ContentFolder = "content";

        private const string ReferenceFolder = "lib";
        private const string ToolsFolder = "tools";
        private const string SourcesFolder = "src";

        // Common item types
        private const string SourcesItemType = "Compile";

        private const string ContentItemType = "Content";
        private const string ProjectReferenceItemType = "ProjectReference";
        private const string ReferenceOutputAssembly = "ReferenceOutputAssembly";
        private const string PackagesFolder = "packages";
        private const string TransformFileExtension = ".transform";

        private MSBuildPackTargetArgs PackTargetArgs { get; set; }

        public void SetIncludeSymbols(bool includeSymbols)
        {
            IncludeSymbols = includeSymbols;
        }
        public bool IncludeSymbols { get; set; }

        public bool IncludeReferencedProjects { get; set; }

        public bool Build { get; set; }

        public Dictionary<string, string> GetProjectProperties()
        {
            return ProjectProperties;
        }
        public Dictionary<string, string> ProjectProperties { get; private set; }

        public bool IsTool { get; set; }
        
        public Common.ILogger Logger
        {
            get
            {
                return _logger ?? Common.NullLogger.Instance;
            }
            set
            {
                _logger = value;
            }
        }

        public Configuration.IMachineWideSettings MachineWideSettings { get; set; }

        public static IProjectFactory ProjectCreator(PackArgs packArgs, string path)
        {
            return new MSBuildProjectFactory()
            {
                IsTool = packArgs.Tool,
                Logger = packArgs.Logger,
                MachineWideSettings = packArgs.MachineWideSettings,
                Build = false,
                IncludeReferencedProjects = packArgs.IncludeReferencedProjects,
                PackTargetArgs = packArgs.PackTargetArgs
            };
        }

        public PackageBuilder CreateBuilder(string basePath, NuGetVersion version, string suffix, bool buildIfNeeded, PackageBuilder builder)
        {
            // Add output files
            ApplyAction(p => p.AddOutputFiles(builder));

            // Add content files if there are any. They could come from a project or nuspec file
            //ApplyAction(p => p.AddFiles(builder, ContentItemType, ContentFolder));

            // Add sources if this is a symbol package
            //if (IncludeSymbols)
            //{
            //    ApplyAction(p => p.AddFiles(builder, SourcesItemType, SourcesFolder));
            //}

            //ProcessDependencies(builder);
            
            return builder;
        }

        private void ApplyAction(Action<MSBuildProjectFactory> action)
        {
            //if (IncludeReferencedProjects)
            //{
            //    RecursivelyApply(action);
            //}
            //else
            //{
            action(this);
            //}
        }

        private void AddOutputFiles(PackageBuilder builder)
        {
            // Get the target framework of the project
            NuGetFramework nugetFramework = null;
            if (builder.TargetFrameworks.Any())
            {
                if (builder.TargetFrameworks.Count > 1)
                {
                    //TODO: throw a proper exception
                    throw new Exception("only allowed to have one framework");
                }
                nugetFramework = builder.TargetFrameworks.First();
            }
            else
            {
                //TODO: throw a proper exception
                throw new Exception("should have atleast one framework in project.json");
            }

            // Get the target file path
            string targetPath = PackTargetArgs.TargetPath;

            // List of extensions to allow in the output path
            var allowedOutputExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                ".dll",
                ".exe",
                ".xml",
                ".winmd"
            };

            if (IncludeSymbols)
            {
                // Include pdbs for symbol packages
                allowedOutputExtensions.Add(".pdb");
            }

            string projectOutputDirectory = Path.GetDirectoryName(targetPath);

            string targetFileName = PackTargetArgs.AssemblyName;

            // By default we add all files in the project's output directory
            foreach (var file in GetFiles(projectOutputDirectory, targetFileName, allowedOutputExtensions, SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);

                // Only look at files we care about
                if (!allowedOutputExtensions.Contains(extension))
                {
                    continue;
                }

                string targetFolder;

                if (IsTool)
                {
                    targetFolder = ToolsFolder;
                }
                else
                {
                    if (Directory.Exists(PackTargetArgs.TargetPath))
                    {
                        targetFolder = Path.Combine(ReferenceFolder, Path.GetDirectoryName(file.Replace(PackTargetArgs.TargetPath, string.Empty)));
                    }
                    else if (nugetFramework == null)
                    {
                        targetFolder = ReferenceFolder;
                    }
                    else
                    {
                        string shortFolderName = nugetFramework.GetShortFolderName();
                        targetFolder = Path.Combine(ReferenceFolder, shortFolderName);
                    }
                }
                var packageFile = new Packaging.PhysicalPackageFile
                {
                    SourcePath = file,
                    TargetPath = Path.Combine(targetFolder, Path.GetFileName(file))
                };
                AddFileToBuilder(builder, packageFile);
            }
        }

        private static IEnumerable<string> GetFiles(string path, string fileNameWithoutExtension, HashSet<string> allowedExtensions, SearchOption searchOption)
        {
            return allowedExtensions.Select(extension => Directory.GetFiles(path, fileNameWithoutExtension + extension, searchOption)).SelectMany(a => a);
        }

        private void AddFileToBuilder(PackageBuilder builder, PhysicalPackageFile packageFile)
        {
            if (!builder.Files.Any(p => packageFile.Path.Equals(p.Path, StringComparison.OrdinalIgnoreCase)))
            {
                builder.Files.Add(packageFile);
            }
            else
            {
                //TODO:  log warning : File '{0}' is not added because the package already contains file '{1}'
            }
        }
    }
}
