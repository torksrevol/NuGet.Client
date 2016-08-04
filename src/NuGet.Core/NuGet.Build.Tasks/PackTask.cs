using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
using NuGet.Frameworks;

namespace NuGet.Build.Tasks
{
    public class PackTask : Microsoft.Build.Utilities.Task
    {

        //TODO: Add PackageTypes
        [Required]
        public ITaskItem PackItem { get; set; }
        public ITaskItem[] Content { get; set; }
        public ITaskItem[] PackContent { get; set; }
        public ITaskItem Id { get; set; }
        public ITaskItem Version { get; set; }
        public ITaskItem Authors { get; set; }
        public ITaskItem Owners { get; set; }
        public ITaskItem Description { get; set; }
        public ITaskItem Copyright { get; set; }
        public ITaskItem Summary { get; set; }
        public ITaskItem RequireLicenseAcceptance { get; set; }
        public ITaskItem LicenseUrl { get; set; }
        public ITaskItem ProjectUrl { get; set; }
        public ITaskItem IconUrl { get; set; }
        public ITaskItem Tags { get; set; }
        public ITaskItem ReleaseNotes { get; set; }
        public ITaskItem Properties { get; set; }
        public ITaskItem Configuration { get; set; }
        public ITaskItem OutputPath { get; set; }
        public ITaskItem TargetPath { get; set; }

        public string Exclude { get; set; }


        
        public override bool Execute()
        {
#if DEBUG
            System.Diagnostics.Debugger.Launch();
#endif
            var packArgs = GetPackArgs();
            var packageBuilder = GetPackageBuilder();
            ProcessJsonFile(packageBuilder, packArgs);
            PackCommandRunner packRunner = new PackCommandRunner(packArgs, null);
            packRunner.BuildPackage(packageBuilder);
            return true;
        }

        private PackArgs GetPackArgs()
        {
            var packArgs = new PackArgs();
            packArgs.Logger = new MSBuildLogger(Log);
            InitCurrentDirectoryAndFileName(packArgs);
            if (Properties != null)
            {
                foreach (var property in Properties.ItemSpec.Split(';'))
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

        private PackageBuilder GetPackageBuilder()
        {
            PackageBuilder builder = new PackageBuilder();
            builder.Id = Id.ItemSpec;
            if (Version != null)
            {
                NuGetVersion version;
                if (!NuGetVersion.TryParse(Version.ItemSpec, out version))
                {
                    throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, "Version string specified '{0}' is invalid.", Version.ItemSpec));
                }
                builder.Version = version;
            }
            else
            {
                builder.Version = new NuGetVersion("1.0.0");
            }
            builder.Owners.AddRange(Owners?.ItemSpec.Split(new char[] {',', ';'}, StringSplitOptions.RemoveEmptyEntries));
            builder.Authors.AddRange(Authors?.ItemSpec.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            builder.Description = Description?.ItemSpec;
            builder.Copyright = Copyright?.ItemSpec;
            builder.Summary = Summary?.ItemSpec;
            builder.ReleaseNotes = ReleaseNotes?.ItemSpec;
            builder.Tags.AddRange(Tags?.ItemSpec.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            Uri tempUri;
            if (Uri.TryCreate(LicenseUrl?.ItemSpec, UriKind.Absolute, out tempUri))
            {
                builder.LicenseUrl = tempUri;
            }
            if (Uri.TryCreate(ProjectUrl?.ItemSpec, UriKind.Absolute, out tempUri))
            {
                builder.ProjectUrl = tempUri;
            }
            if (Uri.TryCreate(IconUrl?.ItemSpec, UriKind.Absolute, out tempUri))
            {
                builder.IconUrl = tempUri;
            }
            bool requireLicenseAcceptance;
            Boolean.TryParse(RequireLicenseAcceptance?.ItemSpec, out requireLicenseAcceptance);
            builder.RequireLicenseAcceptance = requireLicenseAcceptance;
            return builder;
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

        private void ProcessJsonFile(PackageBuilder packageBuilder, PackArgs packArgs)
        {
            string path = ProjectJsonPathUtilities.GetProjectConfigPath(packArgs.CurrentDirectory,
                Path.GetFileName(packArgs.Path));
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

                        packageBuilder.TargetFrameworks.Add(framework.FrameworkName);
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
    }
}
