// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.ProjectManagement;
using EnvDTEProject = EnvDTE.Project;

namespace NuGet.PackageManagement.VisualStudio
{
    public interface IProjectSystemCache
    {
        bool TryGetNuGetProject(string name, out NuGetProject nuGetProject);

        bool TryGetDTEProject(string name, out EnvDTEProject project);

        bool TryGetNuGetProjectName(string name, out EnvDTEProjectName EnvDTEProjectName);

        bool TryGetProjectNameByShortName(string name, out EnvDTEProjectName EnvDTEProjectName);

        bool Contains(string name);

        IEnumerable<NuGetProject> GetNuGetProjects();

        IEnumerable<EnvDTEProject> GetEnvDTEProjects();

        bool IsAmbiguous(string shortName);

        bool AddProject(EnvDTEProjectName projectName, EnvDTEProject project, NuGetProject nuGetProject);

        void RemoveProject(string name);

        void Clear();
    }
}
