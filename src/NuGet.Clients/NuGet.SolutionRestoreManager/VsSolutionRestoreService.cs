// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using NuGet.Frameworks;
using NuGet.PackageManagement.VisualStudio;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace NuGet.SolutionRestoreManager
{
    [PartCreationPolicy(CreationPolicy.Shared)]
    [Export(typeof(IVsSolutionRestoreService))]
    public class VsSolutionRestoreService : IVsSolutionRestoreService
    {
        private readonly IProjectSystemCache _projectSystemCache;

        private readonly DTE _dte;

        [ImportingConstructor]
        public VsSolutionRestoreService([Import]IProjectSystemCache projectSystemCache)
        {
            if (projectSystemCache == null)
            {
                throw new ArgumentNullException(nameof(projectSystemCache));
            }

            _projectSystemCache = projectSystemCache;

            _dte = ServiceLocator.GetInstance<DTE>();
        }

        public Task<bool> CurrentRestoreOperation
        {
            get
            {
                return Task.FromResult(true);
            }
        }

        public async Task<bool> NominateProjectAsync(string projectUniqueName, IVsProjectRestoreInfo projectRestoreInfo, CancellationToken token)
        {
            if (string.IsNullOrEmpty(projectUniqueName))
            {
                throw new ArgumentException(ProjectManagement.Strings.Argument_Cannot_Be_Null_Or_Empty, nameof(projectUniqueName));
            }

            if (projectRestoreInfo == null)
            {
                throw new ArgumentNullException(nameof(projectRestoreInfo));
            }

            var packageReferences = new List<PackageReference>();
            var projectReferences = new List<ProjectRestoreReference>();

            foreach(var tfm in projectRestoreInfo.TargetFrameworks.Cast<IVsTargetFrameworkInfo>())
            {
                var framework = NuGetFramework.Parse(tfm.TargetFrameworkMoniker);

                packageReferences.AddRange(
                    tfm.PackageReferences?
                        .Cast<IVsReferenceItem>()
                        .Select(r => ToPackageReference(r, framework)));

                projectReferences.AddRange(
                    tfm.ProjectReferences?
                        .Cast<IVsReferenceItem>()
                        .Select(r => ToProjectRestoreReference(r, framework)));
            }

            return await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var dteProject = _dte.Solution.Item(projectUniqueName);
                var projectName = new EnvDTEProjectName(dteProject);

                var project = new NominatedNuGetProject(
                    projectName.ShortName,
                    projectUniqueName,
                    projectRestoreInfo.BaseIntermediatePath,
                    packageReferences,
                    projectReferences);

                return _projectSystemCache.AddProject(projectName, dteProject, project);
            });
        }

        private static PackageReference ToPackageReference(
            IVsReferenceItem item, NuGetFramework framework)
        {
            var versionText = TryGetProperty(item, "Version");
            var version = NuGetVersion.Parse(versionText);
            var packageId = new PackageIdentity(item.Name, version);
            var packageReference = new PackageReference(packageId, framework);

            return packageReference;
        }

        private static ProjectRestoreReference ToProjectRestoreReference(
            IVsReferenceItem item, NuGetFramework framework)
        {
            var projectPath = TryGetProperty(item, "ProjectFileFullPath");
            return new ProjectRestoreReference
            {
                ProjectUniqueName = item.Name,
                ProjectPath = projectPath
            };
        }

        private static string TryGetProperty(IVsReferenceItem item, string propertyName)
        {
            if (item.Properties == null)
            {
                // this happens in unit tests
                return null;
            }

            try
            {
                IVsReferenceProperty property = item.Properties.Item(propertyName);
                if (property != null)
                {
                    return property.Value;
                }
            }
            catch (ArgumentException)
            {
            }

            return null;
        }
    }
}
