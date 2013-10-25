﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using Microsoft.Win32;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using System.Collections.Generic;
using Microsoft.Web.Administration;
using EnvDTE80;

namespace wwwsysnetpekr.AttachDebug
{
    // free icon: https://www.iconfinder.com/icons/199499/play_social_video_youtube_icon#size=128


    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(GuidList.guidAttachDebugPkgString)]
    public sealed class AttachDebugPackage : Package
    {
        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public AttachDebugPackage()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
        }



        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation
        #region Package Members

        private DTE2 _applicationObject;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            // _applicationObject = (EnvDTE80.DTE2)System.Runtime.InteropServices.Marshal.GetActiveObject("VisualStudio.DTE.11.0");

            _applicationObject = (EnvDTE80.DTE2)this.GetService(typeof(EnvDTE.DTE));

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if ( null != mcs )
            {
                // Create the command for the menu item.
                CommandID menuCommandID = new CommandID(GuidList.guidAttachDebugCmdSet, (int)PkgCmdIDList.cmdidAttachDebug);
                MenuCommand menuItem = new MenuCommand(MenuItemCallback, menuCommandID );
                mcs.AddCommand( menuItem );
            }
        }
        #endregion

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            List<int> currentWorkerProcess = RecycleAppPool();
            if (currentWorkerProcess == null)
            {
                return;
            }

            AttachToW3wp(currentWorkerProcess);
        }

        void ShowMessage(string txt)
        {
            // Show a Message Box to prove we were here
            IVsUIShell uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
            Guid clsid = Guid.Empty;
            int result;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(uiShell.ShowMessageBox(
                       0,
                       ref clsid,
                       "AttachDebug",
                       string.Format(CultureInfo.CurrentCulture, txt, this.ToString()),
                       string.Empty,
                       0,
                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                       OLEMSGICON.OLEMSGICON_INFO,
                       0,        // false
                       out result));
        }


        private List<int> RecycleAppPool()
        {
            string metabasePath = GetProjectItemValue("WebApplication.AspnetCompilerIISMetabasePath");

            if (string.IsNullOrEmpty(metabasePath) == true)
            {
                ShowMessage("Startup project is not a web project type or \"Use Local IIS Web server\" is not checked.");
                return null;
            }

            return StartAppPool(metabasePath);
        }

        private static List<int> StartAppPool(string appPoolToRecycle)
        {
            string appPoolName = string.Empty;
            List<int> currentProcessList = new List<int>();

            using (ServerManager svr = new ServerManager())
            {
                foreach (Site site in svr.Sites)
                {
                    foreach (Application app in site.Applications)
                    {
                        string sitePath = string.Format("/LM/W3SVC/{0}/ROOT{1}/",
                            site.Id, app.Path);

                        if (sitePath == appPoolToRecycle)
                        {
                            appPoolName = app.ApplicationPoolName;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(appPoolName) == false)
                    {
                        break;
                    }
                }

                if (string.IsNullOrEmpty(appPoolName) == true)
                {
                    return currentProcessList;
                }

                foreach (ApplicationPool appPool in svr.ApplicationPools)
                {
                    if (appPool.Name == appPoolName)
                    {
                        foreach (var item in appPool.WorkerProcesses)
                        {
                            currentProcessList.Add(item.ProcessId);
                        }

                        try
                        {
                            appPool.Recycle();
                        }
                        catch { }
                    }
                }
            }

            return currentProcessList;
        }

        string GetProjectItemValue(string propName)
        {
            string uniqueName = null;

            try
            {
                uniqueName = (string)((object[])(_applicationObject.Solution.SolutionBuild.StartupProjects))[0];
            }
            catch (NullReferenceException)
            {
                return string.Empty;
            }

            Project prj = GetProject(_applicationObject.Solution, uniqueName);
            if (prj == null)
            {
                return string.Empty;
            }

            foreach (var prop in prj.Properties)
            {
                Property property = prop as Property;
                if (property.Name == propName)
                {
                    return property.Value as string;
                }
            }

            return string.Empty;
        }

        private void AttachToW3wp(List<int> currentWorkerProcess)
        {
            System.Diagnostics.Trace.WriteLine("AttachToW3wp ...");

            foreach (var proc in _applicationObject.Debugger.LocalProcesses)
            {
                EnvDTE90.Process3 process = proc as EnvDTE90.Process3;
                if (process == null)
                {
                    continue;
                }

                // Skip process in recycle.
                if (currentWorkerProcess.Contains(process.ProcessID) == true)
                {
                    continue;
                }

                if (process.Name.IndexOf("W3WP.EXE", 0, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    bool attached = false;
                    System.Diagnostics.Trace.WriteLine("Attaching to: " + process.Name);
                    try
                    {
                        process.Attach();
                        attached = true;
                        System.Diagnostics.Trace.WriteLine("Attached to: " + process.Name);
                    }
                    catch
                    {
                    }

                    if (attached == true)
                    {
                        string startupUrl = GetProjectItemValue("WebApplication.IISUrl");
                        if (string.IsNullOrEmpty(startupUrl) == false)
                        {
                            RunInternetExplorer(startupUrl);
                        }
                    }

                    break;
                }
            }
        }

        // http://social.msdn.microsoft.com/Forums/vstudio/en-US/36adcd56-5698-43ca-bcba-4527daabb2e3/finding-the-startup-project-in-a-complicated-solution
        public static Project GetProject(Solution solution, string uniqueName)
        {
            Project ret = null;

            if (solution != null && uniqueName != null)
            {
                foreach (Project p in solution.Projects)
                {
                    ret = GetSubProject(p, uniqueName);

                    if (ret != null)
                        break;
                }
            }

            return ret;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public const string vsProjectKindSolutionItems = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        // http://social.msdn.microsoft.com/Forums/vstudio/en-US/36adcd56-5698-43ca-bcba-4527daabb2e3/finding-the-startup-project-in-a-complicated-solution
        public static Project GetSubProject(Project project, string uniqueName)
        {
            Project ret = null;

            if (project != null)
            {
                if (project.UniqueName == uniqueName)
                {
                    ret = project;
                }
                else if (project.Kind == vsProjectKindSolutionItems)
                {
                    // Solution folder  
                    foreach (ProjectItem projectItem in project.ProjectItems)
                    {
                        ret = GetSubProject(projectItem.SubProject, uniqueName);

                        if (ret != null)
                            break;
                    }
                }
            }

            return ret;
        }

        private void RunInternetExplorer(string url)
        {
            System.Diagnostics.Process.Start(url);
        }
    }
}
