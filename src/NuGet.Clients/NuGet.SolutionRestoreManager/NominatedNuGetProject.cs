// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;

namespace NuGet.SolutionRestoreManager
{
    public class NominatedNuGetProject : NuGetProject, INuGetIntegratedProject, IDependencyGraphProject
    {
        private readonly string _projectName;
        private readonly string _projectUniqueName;
        private readonly string _projectFullPath;
        private readonly string _baseIntermediatePath;

        private readonly List<PackageReference> _installedPackages;
        private readonly List<ProjectRestoreReference> _directProjectReferences;

        public NominatedNuGetProject(
            string projectName,
            string projectUniqueName,
            string baseIntermediatePath,
            IEnumerable<PackageReference> packageReferences,
            IEnumerable<ProjectRestoreReference> projectReferences)
        {
            _projectName = projectName;
            _projectUniqueName = projectUniqueName;
            _projectFullPath = projectUniqueName;
            _baseIntermediatePath = baseIntermediatePath;
            _installedPackages = packageReferences.ToList();
            _directProjectReferences = projectReferences.ToList();

            InternalMetadata.Add(NuGetProjectMetadataKeys.Name, _projectName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.FullPath, _projectFullPath);
            InternalMetadata.Add(NuGetProjectMetadataKeys.UniqueName, _projectUniqueName);
        }

        #region IDependencyGraphProject
        
        /// <summary>
        /// Making this timestamp as the current time means that a restore with this project in the graph
        /// will never no-op. We do this to keep this work-around implementation simple.
        /// </summary>
        public DateTimeOffset LastModified => DateTimeOffset.Now;

        public string MSBuildProjectPath => _projectFullPath;

        public IReadOnlyList<PackageSpec> GetPackageSpecsForRestore(ExternalProjectReferenceContext context)
        {
            var tfis = _installedPackages
                .GroupBy(p => p.TargetFramework)
                .Select(g => ToTargetFrameworkInformation(g.Key, g))
                .ToList();

            var packageSpec = new PackageSpec(tfis)
            {
                Name = _projectName,
                FilePath = _projectFullPath,
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    OutputType = RestoreOutputType.NETCore,
                    ProjectPath = _projectFullPath,
                    ProjectName = _projectName,
                    ProjectUniqueName = _projectUniqueName,
                    ProjectReferences = _directProjectReferences
                }
            };

            return new[] { packageSpec };
        }

        private static TargetFrameworkInformation ToTargetFrameworkInformation(
            NuGetFramework framework, IEnumerable<PackageReference> packageReferences)
        {
            var tfi = new TargetFrameworkInformation
            {
                FrameworkName = framework
            };

            return tfi;
        }

        public Task<IReadOnlyList<ExternalProjectReference>> GetProjectReferenceClosureAsync(
            ExternalProjectReferenceContext context)
        {
            throw new NotImplementedException();
        }

        public bool IsRestoreRequired(IEnumerable<VersionFolderPathResolver> pathResolvers, ISet<PackageIdentity> packagesChecked, ExternalProjectReferenceContext context)
        {
            // TODO: when the real implementation of NuGetProject for CPS PackageReference is completed, more
            // sophisticated restore no-op detection logic is required. Always returning true means that every build
            // will result in a restore.
            return true;
        }

        #endregion

        #region NuGetProject

        public override Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            return Task.FromResult<IEnumerable<PackageReference>>(_installedPackages);
        }

        public override Task<bool> InstallPackageAsync(PackageIdentity packageIdentity, DownloadResourceResult downloadResourceResult, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public override Task<bool> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
