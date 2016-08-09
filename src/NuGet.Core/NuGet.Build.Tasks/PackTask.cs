using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
using NuGet.Frameworks;
using NuGet.Packaging.Core;

namespace NuGet.Build.Tasks
{
    public class PackTask : Microsoft.Build.Utilities.Task
    {

        //TODO: Add PackageTypes
        //TODO: Add support for Symbols
        //TODO: Add support for Tools
        //TODO: Add support for Repository
        [Required]
        public ITaskItem PackItem { get; set; }
        public ITaskItem[] PackageFiles { get; set; }
        public ITaskItem[] TargetFrameworks { get; set; }
        public ITaskItem[] PackageTypes { get; set; }
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string Authors { get; set; }
        public string Owners { get; set; }
        public string Description { get; set; }
        public string Copyright { get; set; }
        public string Summary { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public string LicenseUrl { get; set; }
        public string ProjectUrl { get; set; }
        public string IconUrl { get; set; }
        public string Tags { get; set; }
        public string ReleaseNotes { get; set; }
        public string Properties { get; set; }
        public string Configuration { get; set; }
        public string OutputPath { get; set; }
        public string[] TargetPath { get; set; }
        public string AssemblyName { get; set; }
        public string Exclude { get; set; }
        public string PackageOutputPath { get; set; }
        public bool IsTool { get; set; }
        public bool IncludeSymbols { get; set; }
        public ITaskItem[] ProjectReferences { get; set; }
        


        
        public override bool Execute()
        {
#if DEBUG
            System.Diagnostics.Debugger.Launch();
#endif
            var packArgs = GetPackArgs();
            var packageBuilder = GetPackageBuilder(packArgs);
            var contentFiles = ProcessContentToIncludeInPackage();
            packArgs.PackTargetArgs.ContentFiles = contentFiles;
            ProcessJsonFile(packageBuilder, packArgs.CurrentDirectory,
                Path.GetFileName(packArgs.Path), isHostProject:true);
            PackCommandRunner packRunner = new PackCommandRunner(packArgs, MSBuildProjectFactory.ProjectCreator, packageBuilder);
            packRunner.BuildPackage();
            return true;
        }

        private PackArgs GetPackArgs()
        {
            var packArgs = new PackArgs();
            packArgs.Logger = new MSBuildLogger(Log);
            packArgs.OutputDirectory = PackageOutputPath;
            packArgs.Tool = IsTool;
            packArgs.Symbols = IncludeSymbols;
            packArgs.PackTargetArgs = new MSBuildPackTargetArgs()
            {
                TargetPath = TargetPath,
                Configuration = Configuration,
                OutputPath = OutputPath,
                AssemblyName = AssemblyName
            };
            packArgs.PackTargetArgs.TargetFrameworks = ParseFrameworks();
            
            InitCurrentDirectoryAndFileName(packArgs);
            if (Properties != null)
            {
                foreach (var property in Properties.Split(';'))
                {
                    int index = property.IndexOf('=');
                    if (index > 0 && index < property.Length - 1)
                    {
                        packArgs.Properties.Add(property.Substring(0, index), property.Substring(index + 1));
                    }
                }
            }

            PackCommandRunner.SetupCurrentDirectory(packArgs);

            return packArgs;
        }

        private ISet<NuGetFramework> ParseFrameworks()
        {
            var nugetFrameworks = new HashSet<NuGetFramework>();
            if (TargetFrameworks != null)
            {
                nugetFrameworks = new HashSet<NuGetFramework>(TargetFrameworks.Select(t => NuGetFramework.Parse(t.ItemSpec)));
            }
            return nugetFrameworks;
        } 

        private PackageBuilder GetPackageBuilder(PackArgs packArgs)
        {
            PackageBuilder builder = new PackageBuilder();
            builder.Id = PackageId;
            if (PackageVersion != null)
            {
                NuGetVersion version;
                if (!NuGetVersion.TryParse(PackageVersion, out version))
                {
                    throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, "PackageVersion string specified '{0}' is invalid.", PackageVersion));
                }
                builder.Version = version;
            }
            else
            {
                builder.Version = new NuGetVersion("1.0.0");
            }
            if (Owners != null)
            {
                builder.Owners.AddRange(Owners?.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            }

            if (Authors != null)
            {
                builder.Authors.AddRange(Authors?.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            }

            if (Tags != null)
            {
                builder.Tags.AddRange(Tags?.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            }

            builder.Description = Description;
            builder.Copyright = Copyright;
            builder.Summary = Summary;
            builder.ReleaseNotes = ReleaseNotes;
            Uri tempUri;
            if (Uri.TryCreate(LicenseUrl, UriKind.Absolute, out tempUri))
            {
                builder.LicenseUrl = tempUri;
            }
            if (Uri.TryCreate(ProjectUrl, UriKind.Absolute, out tempUri))
            {
                builder.ProjectUrl = tempUri;
            }
            if (Uri.TryCreate(IconUrl, UriKind.Absolute, out tempUri))
            {
                builder.IconUrl = tempUri;
            }
            builder.RequireLicenseAcceptance = RequireLicenseAcceptance;
            builder.PackageTypes = ParsePackageTypes();
            ParseProjectToProjectReferences(builder, packArgs);
            return builder;
        }

        private ICollection<PackageType> ParsePackageTypes()
        {
            var listOfPackageTypes = new List<PackageType>();
            if (PackageTypes != null)
            {
                foreach (var packageType in PackageTypes)
                {
                    var customMetadata = packageType.CloneCustomMetadata();
                    string typeName = packageType.ItemSpec;
                    var version = PackageType.EmptyVersion;
                    if (customMetadata.Contains("Version"))
                    {
                        string versionString = packageType.GetMetadata("Version");
                        Version.TryParse(versionString, out version);
                    }
                    listOfPackageTypes.Add(new PackageType(typeName, version));
                }
            }
            return listOfPackageTypes;
        }

        private void ParseProjectToProjectReferences(PackageBuilder packageBuilder, PackArgs packArgs)
        {
            var dependencies = new HashSet<PackageDependency>();
            var projectReferences = new List<ProjectToProjectReference>();
            if (ProjectReferences != null)
            {
                foreach (var p2pReference in ProjectReferences)
                {
                    var referenceString = p2pReference.ItemSpec;
                    string[] param = referenceString.Split(new char[] {'|'}, StringSplitOptions.RemoveEmptyEntries);
                    if (param[0] == "PROJECT")
                    {
                        // This is a project reference, and the DLL should be copied over to lib.
                        projectReferences.Add(new ProjectToProjectReference()
                        {
                            TargetPath = param[1],
                            AssemblyName = param[2]
                        });
                        string projectPath = param[3];
                        ProcessJsonFile(packageBuilder, Path.GetDirectoryName(projectPath), Path.GetFileName(projectPath), isHostProject:false);
                    }
                    else if (param[0] == "PACKAGE")
                    {
                        // This is to be treated as a nupkg dependency, add as library dependency.
                        var packageId = param[1];

                        //TODO: Do the work to get the version from AssemblyInfo.cs

                        var version = "1.0.0";
                        if (param.Count() == 3)
                        {
                            version = param[2];
                        }
                        var packageDependency = new PackageDependency(packageId, VersionRange.Parse(version));
                        dependencies.Add(packageDependency);
                    }
                }

                packageBuilder.DependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.AnyFramework, dependencies));
                packArgs.PackTargetArgs.ProjectReferences = projectReferences;
            }
        }
        private void InitCurrentDirectoryAndFileName(PackArgs packArgs)
        {
            if (PackItem == null)
            {
                throw new InvalidOperationException(nameof(PackItem));
            }
            else
            {
                packArgs.CurrentDirectory = Path.Combine(PackItem.GetMetadata("RootDir"), PackItem.GetMetadata("Directory"));
                packArgs.Arguments = new string[] {string.Concat(PackItem.GetMetadata("FileName"), PackItem.GetMetadata("Extension"))};
                packArgs.Path = PackItem.GetMetadata("FullPath");
                var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Exclude != null)
                {
                    exclude.AddRange(Exclude.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                
                packArgs.Exclude = exclude;
            }
        }

        private void ProcessJsonFile(PackageBuilder packageBuilder, string currentDirectory, string projectFileName, bool isHostProject)
        {
            string path = ProjectJsonPathUtilities.GetProjectConfigPath(currentDirectory, projectFileName);
            if (!File.Exists(path))
            {
                return;
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var spec = JsonPackageSpecReader.GetPackageSpec(stream, packageBuilder.Id, path, null);
                if (spec.TargetFrameworks.Any())
                {
                    foreach (var framework in spec.TargetFrameworks)
                    {
                        if (framework.FrameworkName.IsUnsupported)
                        {
                            throw new Exception(String.Format(CultureInfo.CurrentCulture, NuGet.Commands.Strings.Error_InvalidTargetFramework, framework.FrameworkName));
                        }
                        if (isHostProject)
                        {
                            packageBuilder.TargetFrameworks.Add(framework.FrameworkName);
                        }
                        PackCommandRunner.AddDependencyGroups(framework.Dependencies.Concat(spec.Dependencies), framework.FrameworkName, packageBuilder);
                    }
                }
                else
                {
                    if (spec.Dependencies.Any())
                    {
                        PackCommandRunner.AddDependencyGroups(spec.Dependencies, NuGetFramework.AnyFramework, packageBuilder);
                    }
                }
            }
        }

        private Dictionary<string, HashSet<string>> ProcessContentToIncludeInPackage()
        {
            // This maps from source path on disk to target path inside the nupkg.
            var fileModel = new Dictionary<string, HashSet<string>>();
            if (PackageFiles != null)
            {
                var excludeFiles = CalculateFilesToExcludeInPack();
                foreach (var packageFile in PackageFiles)
                {
                    string targetPath = string.Empty;
                    var customMetadata = packageFile.CloneCustomMetadata();
                    var sourcePath = GetSourcePath(packageFile, customMetadata);
                    if (excludeFiles.Contains(sourcePath))
                    {
                        continue;
                    }
                    if (customMetadata.Contains("PackagePath"))
                    {
                        targetPath = packageFile.GetMetadata("PackagePath");
                    }

                    if (fileModel.ContainsKey(sourcePath))
                    {
                        var setOfTargetPaths = fileModel[sourcePath];
                        if (!setOfTargetPaths.Contains(targetPath))
                        {
                            setOfTargetPaths.Add(targetPath);
                        }
                    }
                    else
                    {
                        var setOfTargetPaths = new HashSet<string>();
                        setOfTargetPaths.Add(targetPath);
                        fileModel.Add(sourcePath, setOfTargetPaths);
                    }
                }
            }

            return fileModel;
        }

        private string GetSourcePath(ITaskItem packageFile, IDictionary customMetadata)
        {
            string sourcePath = packageFile.GetMetadata("FullPath");
            if (customMetadata.Contains("MSBuildSourceProjectFile"))
            {
                string sourceProjectFile = packageFile.GetMetadata("MSBuildSourceProjectFile");
                string identity = packageFile.GetMetadata("Identity");
                sourcePath = Path.Combine(sourceProjectFile.Replace(Path.GetFileName(sourceProjectFile), string.Empty), identity);
            }
            return Path.GetFullPath(sourcePath);
        }

        private ISet<string> CalculateFilesToExcludeInPack()
        {
            var excludeFiles = new HashSet<string>();
            foreach (var file in PackageFiles)
            {
                var customMetadata = file.CloneCustomMetadata();
                string sourcePath = GetSourcePath(file,  customMetadata);
                if (ExcludeInPack(file, customMetadata))
                {
                    excludeFiles.Add(sourcePath);
                }
            }
            return excludeFiles;
        }

        private bool ExcludeInPack(ITaskItem packageFile, IDictionary customMetadata)
        {
            if (customMetadata.Contains("Pack"))
            {
                string shouldPack = packageFile.GetMetadata("Pack");
                bool pack;
                Boolean.TryParse(shouldPack, out pack);
                return !pack;
            }
            return false;
        }
    }
}
