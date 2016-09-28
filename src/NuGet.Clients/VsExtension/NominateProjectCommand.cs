//------------------------------------------------------------------------------
// <copyright file="NominateProject.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.PackageManagement.VisualStudio;
using NuGet.SolutionRestoreManager;
using Microsoft.VisualStudio.ComponentModelHost;
using System.Diagnostics;
using NuGet.ProjectManagement;
using Strings = NuGet.PackageManagement.VisualStudio.Strings;
using System.Threading;
using NuGet.ProjectModel;
using System.Collections.Generic;

namespace NuGetVSExtension
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class NominateProjectCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("ff3f79db-6281-4f6c-abd0-079f9d49c962");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        private readonly IProjectSystemCache _projectSystemCache;

        private readonly IVsSolutionRestoreService _solutionRestoreService;

        /// <summary>
        /// Initializes a new instance of the <see cref="NominateProjectCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private NominateProjectCommand(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            this.package = package;

            var commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
                commandService.AddCommand(menuItem);
            }

            var componentModel = ServiceProvider.GetService(typeof(SComponentModel)) as IComponentModel;

            _projectSystemCache = componentModel?.GetService<IProjectSystemCache>();
            Trace.WriteLineIf(_projectSystemCache != null, "Cache is found");

            _solutionRestoreService = componentModel?.GetService<IVsSolutionRestoreService>();
            Trace.WriteLineIf(_solutionRestoreService != null, "Restore Service is found");
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static NominateProjectCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        private uint _solutionNotBuildingAndNotDebuggingContextCookie;
        private IVsMonitorSelection _vsMonitorSelection;

        private IVsMonitorSelection VsMonitorSelection
        {
            get
            {
                if (_vsMonitorSelection == null)
                {
                    // get the UI context cookie for the debugging mode
                    _vsMonitorSelection = (IVsMonitorSelection)ServiceProvider.GetService(typeof(IVsMonitorSelection));

                    // get the solution not building and not debugging cookie
                    Guid guid = VSConstants.UICONTEXT.SolutionExistsAndNotBuildingAndNotDebugging_guid;
                    _vsMonitorSelection.GetCmdUIContextCookie(ref guid, out _solutionNotBuildingAndNotDebuggingContextCookie);
                }
                return _vsMonitorSelection;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new NominateProjectCommand(package);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // *** temp code
                var project = EnvDTEProjectUtility.GetActiveProject(VsMonitorSelection);

                if (project != null
                    &&
                    !EnvDTEProjectUtility.IsUnloaded(project)
                    &&
                    EnvDTEProjectUtility.IsSupported(project))
                {
                    var hierarchy = VsHierarchyUtility.ToVsHierarchy(project);
                    Trace.WriteLineIf(hierarchy.IsCapabilityMatch("CPS"), $"'{project.Name}' is a CPS project");
                    //if (hierarchy.IsCapabilityMatch("CPS"))
                    {
                        var _dependencyGraphProjectCache = new Dictionary<string, DependencyGraphProjectCacheEntry>(StringComparer.Ordinal);
                        var referenceContext = new ExternalProjectReferenceContext(
                            _dependencyGraphProjectCache,
                            logger: NuGet.Common.NullLogger.Instance);

                        //EnvDTE.Project dteProject = null;
                        //if (_projectSystemCache.TryGetDTEProject(project.Name, out dteProject))
                        {
                            var nugetProject = MSBuildShellOutNuGetProject.Create(project);
                            var specs = nugetProject.GetPackageSpecsForRestore(referenceContext);
                            //var packages = await nugetProject.GetInstalledPackagesAsync(CancellationToken.None);
                        }
                    }
                }
                else
                {
                    // show error message when no supported project is selected.
                    string projectName = project != null ? project.Name : String.Empty;

                    string errorMessage = String.IsNullOrEmpty(projectName)
                        ? Resources.NoProjectSelected
                        : String.Format(CultureInfo.CurrentCulture, Strings.DTE_ProjectUnsupported, projectName);

                    MessageHelper.ShowWarningMessage(errorMessage, Resources.ErrorDialogBoxTitle);
                }
            });
        }

        private void BeforeQueryStatusForAddPackageDialog(object sender, EventArgs args)
        {
            var command = (OleMenuCommand)sender;
            if (command != null)
            {
                command.Visible = true;
                command.Enabled = true;
                command.Text = "Nominate project";
            };
        }
    }
}
