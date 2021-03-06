﻿/***************************************************************************

Copyright (c) Microsoft Corporation. All rights reserved.
This code is licensed under the Visual Studio SDK license terms.
THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.

***************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.IO;
using System.Security;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TemplateWizard;
using EnvDTE;
using VSLangProj;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.BuildEngine;
//using Microsoft.Build.Evaluation;
using Silversite.Services;

namespace XamlImageConverter {
	/// <summary>
	/// We need to override the default Wizard used by the WebSite Project
	/// in order to enable the itemtemplate to add the App_Code folder. That means that we will override the 
	/// ShouldAddProjectItem to return true when the item to be added is the App_Code folder. 
	///  In all other methods we will just delegate to the default wizard
	/// </summary>
	public class CustomWizardBase : IWizard {
		#region fields
		IWizard aggregatedWizard;
		#endregion

		#region Properties
		internal protected string AggregatedWizardAssemblyName
		{
			get
			{
				// return 'Microsoft.VisualStudio.Web, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a';
				return null;
			}
		}

		internal protected virtual string AggregatedFullClassName
		{
			get
			{
				return "";
			}
		}

		internal protected IWizard AggregatedWizard
		{
			get
			{
				if (aggregatedWizard == null) {
					LoadAggregatedWizard();
				}
				return aggregatedWizard;
			}
		}
		#endregion

		#region IWizard Members

		public virtual void BeforeOpeningFile(EnvDTE.ProjectItem projectItem) {
			AggregatedWizard.BeforeOpeningFile(projectItem);
		}

		public virtual void ProjectFinishedGenerating(EnvDTE.Project project) {
			AggregatedWizard.ProjectFinishedGenerating(project);
		}

		public virtual void ProjectItemFinishedGenerating(EnvDTE.ProjectItem projectItem) {
			AggregatedWizard.ProjectItemFinishedGenerating(projectItem);
		}

		public virtual void RunFinished() {
			AggregatedWizard.RunFinished();
		}

		public virtual void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams) {
			AggregatedWizard.RunStarted(automationObject, replacementsDictionary, runKind, customParams);
		}

		/// <summary>
		/// Override original logic that determines if project items should be added.
		/// We want to return true if the add App_Code directory is added to project root
		/// </summary>
		/// <param name="filePath">path to file</param>
		/// <returns>true if file should be added</returns>
		public virtual bool ShouldAddProjectItem(string filePath) {
			return AggregatedWizard.ShouldAddProjectItem(filePath);
		}

		#endregion

		#region Helper methods
		protected void LoadAggregatedWizard() {
			Assembly asm = Assembly.Load(AggregatedWizardAssemblyName);
			aggregatedWizard = (IWizard)asm.CreateInstance(AggregatedFullClassName);
		}
		#endregion
	}


	public class ItemWizard : IWizard {

		EnvDTE.ProjectItem item;
		string msbuild;

		public virtual void ProjectItemFinishedGenerating(EnvDTE.ProjectItem projectItem) {
			projectItem.Properties.Item("ItemType").Value = "XamlConvert";
			projectItem.Properties.Item("CustomTool").Value = "";
			item = projectItem;
		}

		const string target = "XamlImageConverter.targets";
		const string MSBuildExtensionsPath = @"$(MSBuildExtensionsPath)\johnshope.com\XamlImageConverter";

		public virtual void RunFinished() {
			try {
				var proj = item.ContainingProject;
				var dte = item.DTE;

				//foreach (var x in proj.Properties.OfType<Property>()) System.Diagnostics.Debugger.Log(1, "Debug", x.Name + "\n");

				CopyBin();
				ConfigureProject(proj);
				CopyDemo();
				proj.Save();

				dte.ExecuteCommand("File.SaveAll", string.Empty);

				var filename = proj.FullName;
				var bproj = new Microsoft.Build.Evaluation.Project(filename);

				var imppath = Path.Combine(MSBuildExtensionsPath, target);

				if (bproj.Imports.Any(imp => imp.ImportedProject.FullPath == imppath)) return; // import is already there, so no need to modify project any further.

				ModifyWebConfig();

				// select project
				string solutionName = Path.GetFileNameWithoutExtension(dte.Solution.FullName);
				dte.Windows.Item(EnvDTE.Constants.vsWindowKindSolutionExplorer).Activate();
				((EnvDTE80.DTE2)dte).ToolWindows.SolutionExplorer.GetItem(solutionName + @"\" + proj.Name).Select(vsUISelectionType.vsUISelectionTypeSelect);

				//Because modify the open *.csproj file outside of the IDE will pop up a window to let you reload the file.
				//IExecute Command to unload the project before modifying the *.csproj file. And reload after the *.csproj has already updated.
				bproj.Xml.AddImport(imppath);
				dte.ExecuteCommand("Project.UnloadProject", string.Empty);
				bproj.Save();
				dte.ExecuteCommand("Project.ReloadProject", string.Empty);
			} catch (Exception ex) {
			}
		}

		public void BeforeOpeningFile(EnvDTE.ProjectItem projectItem) {
		}

		public void ProjectFinishedGenerating(EnvDTE.Project project) {
		}

		public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams) {
		}

		public bool ShouldAddProjectItem(string filePath) {
			return true;
		}

		public static readonly string nl = Environment.NewLine;

		public void ConfigureProject(EnvDTE.Project proj) {
			// add imports
			/* var path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			var imppath = Path.Combine(path, target);
			var old = vslang.Imports.OfType<string>()
				.FirstOrDefault(imp => imp.Contains(target));
			if (old != null) vslang.Imports.Remove(old);
			vslang.Imports.Add(imppath);
			vslang.BuildManager. */

			//add reference to XamlImageConverter.Web.dll
			var vslang = (VSLangProj.VSProject)proj.Object;
			var dll = "XamlImageConverter.Web.dll";
			var path = Path.GetDirectoryName(proj.FullName);
			var reference = Path.Combine(path, "bin", dll);
			if (!File.Exists(reference)) {
				path = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).ToString());
				reference = Path.Combine(path, dll);
			}

			var oldref = vslang.References.OfType<VSLangProj.Reference>()
				.FirstOrDefault(r => r.Name == "XamlImagesConverter.Web");
			if (oldref != null) oldref.Remove();

			try {
				vslang.References.Add(reference);
			} catch (Exception ex) {
			}
		}

		public void CopyDemo() {
			var proj = item.ContainingProject.FullName;
			var dest = Path.GetDirectoryName(proj);
			var src = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

			const string DemoFolder = "XamlImageConverter.Demo";
			var demo = Path.Combine(dest, DemoFolder);
			if (!Directory.Exists(demo)) {
				try {
					Directory.CreateDirectory(demo);
					var path = Path.Combine(src, "Demo\\*");
					var vsproj = item.ContainingProject;

					var folder = vsproj.ProjectItems.AddFolder(DemoFolder);

					foreach (var file in Files.All(path)) {
						try {
							var litem = folder.ProjectItems.AddFromFileCopy(file);
							litem.Properties.Item("CustomTool").Value = "";
							if (file.EndsWith(".xaml")) litem.Properties.Item("ItemType").Value = "XamlConvert";
							//else if (file.EndsWith(".xaml")) litem.Properties.Item("ItemType").Value = "Content";
						} catch (Exception ex) {
						}
					}
					// collapse folder
					var solutionExplorer = (UIHierarchy)vsproj.DTE.Windows.Item(Constants.vsext_wk_SProjectWindow).Object;

					if (solutionExplorer.UIHierarchyItems.Count > 0) {
						UIHierarchyItem rootNode = solutionExplorer.UIHierarchyItems.Item(1);

						UIHierarchyItem uiproj = rootNode.UIHierarchyItems.Item(vsproj.Name);
						UIHierarchyItem demoNode = uiproj.UIHierarchyItems.Item(DemoFolder);
						demoNode.UIHierarchyItems.Expanded = false;
					}

					vsproj.Save();
				} catch (Exception ex) {
				}
			}
		}
		class CopyDir {
			public static void Copy(string sourceDirectory, string targetDirectory) {
				DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
				DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

				CopyAll(diSource, diTarget);
			}

			public static void CopyAll(DirectoryInfo source, DirectoryInfo target) {
				Directory.CreateDirectory(target.FullName);

				// Copy each file into the new directory.
				foreach (FileInfo fi in source.GetFiles()) {
					Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
					fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
				}

				// Copy each subdirectory using recursion.
				foreach (DirectoryInfo diSourceSubDir in source.GetDirectories()) {
					DirectoryInfo nextTargetSubDir =
						 target.CreateSubdirectory(diSourceSubDir.Name);
					CopyAll(diSourceSubDir, nextTargetSubDir);
				}
			}

			public static void Main() {
				string sourceDirectory = @"c:\sourceDirectory";
				string targetDirectory = @"c:\targetDirectory";

				Copy(sourceDirectory, targetDirectory);
			}

			// Output will vary based on the contents of the source directory.
		}

		public void CopyBin() {

			var proj = item.ContainingProject;
			var projname = proj.FullName;
			var dest = Path.GetDirectoryName(projname);
			var conf = Path.Combine(dest, "web.config");
			var src = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

			// Copy to MSBuild extensions
			msbuild = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"MSBuild\johnshope.com\XamlImageConverter");
			Directory.CreateDirectory(msbuild);
			/*Files.Copy(Path.Combine(src, "XamlImageConverter.targets"), msbuild);
			Files.Copy(Path.Combine(src, "XamlImageConverter.dll"), msbuild);
			Files.Copy(Path.Combine(src, "XamlImageConverter.pdb"), msbuild);
			Files.Copy(Path.Combine(src, "Awesomium"), msbuild, );
			Files.Copy(Path.Combine(src, "gxps"), msbuild);
			Files.Copy(Path.Combine(src, "html2xaml"), msbuild);
			Files.Copy(Path.Combine(src, "ImageMagick"), msbuild);
			Files.Copy(Path.Combine(src, "psd2xaml"), msbuild);*/
			CopyDirectory.Copy(src, msbuild);


			var IsWeb = File.Exists(conf);
			if (!IsWeb) return;

			// copy lazy dll's
			var bin = Path.Combine(dest, "Bin");
			var Silversite = File.Exists(Path.Combine(bin, "Silversite.Core.dll"));
			string binlazy;
			//if (Silversite) binlazy = Path.Combine(dest, "Silversite\\Bin");
			//else
			binlazy = Path.Combine(bin, "Lazy");
			if (IsWeb && !Directory.Exists(binlazy)) {
				Directory.CreateDirectory(binlazy);
				CopyDirectory.Copy(src, binlazy);
				/*Files.Copy(Path.Combine(src, "XamlImageConverter.dll"), binlazy);
				Files.Copy(Path.Combine(src, "XamlImageConverter.pdb"), binlazy);
				Files.Copy(Path.Combine(src, "Awesomium"), binlazy);
				Files.Copy(Path.Combine(src, "gxps"), binlazy);
				Files.Copy(Path.Combine(src, "html2xaml"), binlazy);
				Files.Copy(Path.Combine(src, "ImageMagick"), binlazy);
				Files.Copy(Path.Combine(src, "psd2xaml"), binlazy);*/
				//var cache = Path.Combine(dest, "Images\\Cache");
				//if (!Directory.Exists(cache)) Directory.CreateDirectory(cache);
			}

			// copy XamlImageConverter.Web.dll when not using Silversite.
			if (!Silversite && IsWeb) {
				Files.Copy(Path.Combine(src, "XamlImageConverter.Web.*"), bin);
			}
		}

		public void ModifyWebConfig() {
			var proj = item.ContainingProject;
			var projname = proj.FullName;
			var dest = Path.GetDirectoryName(projname);
			var conf = Path.Combine(dest, "web.config");
			var src = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			var bin = Path.Combine(dest, "Bin");

			var IsWeb = File.Exists(conf);
			if (!IsWeb) return;

			// save & close opened web.config
			var wc = proj.ProjectItems.OfType<EnvDTE.ProjectItem>()
				.FirstOrDefault(it => it.Name.ToLower() == "web.config");
			if (wc.Document != null) {
				wc.Document.Save();
				wc.Document.Close();
			}

			// modify web.config
			var webconfig = XElement.Load(conf);

			var Silversite = File.Exists(Path.Combine(bin, "Silversite.Core.dll"));
			if (!Silversite) {
				//configSections
				XElement configSections = webconfig.Element("configSections");
				if (configSections == null) webconfig.AddFirst(configSections = new XElement("configSections"));

				//configSections.Elements().Where(x => ((string)x.Attribute("type") ?? "").Contains("PublicKeyToken=60c2ec984bc1bb45")).Remove();
				configSections.Add(XElement.Parse("<section name='XamlImageConverter' type='XamlImageConverter.Configuration, 	XamlImageConverters.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"));
				// XamlImageConverter section
				if (webconfig.Element("XamlImageConverter") == null) configSections.AddAfterSelf(XElement.Parse("<XamlImageConverter Log='true' Cache='~/Images/Cache' />"));
			}

			// system.webServer
			var server = webconfig.Element("system.webServer");
			if (server == null) webconfig.Add(server = new XElement("system.webServer"));
			var handlers = server.Element("handlers");
			if (handlers == null) server.Add(handlers = new XElement("handlers"));
			handlers.Elements().Where(x => ((string)x.Attribute("type") ?? "").Contains("PublicKeyToken=60c2ec984bc1bb45")).Remove();
			handlers.Add(XElement.Parse("<add name='XamlImageConverter.Xaml' verb='*' path='*.xaml' preCondition='integratedMode' type='XamlImageConverter.Web.XamlImageHandler, XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"),
					XElement.Parse("<add name='XamlImageConverter.Svg' verb='*' path='*.svg' preCondition='integratedMode' type='XamlImageConverter.Web.XamlImageHandler, XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"),
					XElement.Parse("<add name='XamlImageConverter.Svgz' verb='*' path='*.svgz' preCondition='integratedMode' type='XamlImageConverter.Web.XamlImageHandler, XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"),
					XElement.Parse("<add name='XamlImageConverter.Psd' verb='*' path='*.psd' preCondition='integratedMode' type='XamlImageConverter.Web.XamlImageHandler, XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"),
					XElement.Parse("<add name='XamlImageConverter.Dynamic' verb='*' path='xic.axd' preCondition='integratedMode' type='XamlImageConverter.Web.XamlImageHandler, XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"));

			// system.web
			var web = webconfig.Element("system.web");
			if (web == null) server.AddBeforeSelf(web = new XElement("system.web"));

			// compilation
			var comp = web.Element("compilation");
			if (comp == null) web.AddFirst(comp = new XElement("compilation"));
			var assemblies = comp.Element("assemblies");
			if (assemblies == null) comp.Add(assemblies = new XElement("assemblies"));
			assemblies.Elements().Where(x => ((string)x.Attribute("assembly") ?? "").Contains("PublicKeyToken=60c2ec984bc1bb45")).Remove();
			assemblies.Add(XElement.Parse("<add assembly='XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45'/>"));

			// pages
			var pages = web.Element("pages");
			if (pages == null) comp.AddAfterSelf(pages = new XElement("pages"));
			var controls = pages.Element("controls");
			if (controls == null) pages.Add(controls = new XElement("controls"));
			controls.Elements().Where(x => ((string)x.Attribute("assembly") ?? "").Contains("PublicKeyToken=60c2ec984bc1bb45")).Remove();
			controls.Add(XElement.Parse("<add tagPrefix='xic' namespace='XamlImageConverter.Web.UI' assembly='XamlImageConverter.Web, Version=3.2.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"));

			/*
			// httpHandlers
			var httpHandlers = web.Element("httpHandlers");
			if (httpHandlers == null) web.Add(httpHandlers = new XElement("httpHandlers"));
			httpHandlers.Elements().Where(x => ((string)x.Attribute("type") ?? "").Contains("PublicKeyToken=60c2ec984bc1bb45")).Remove();
			httpHandlers.Add(new XComment("<add verb='*' path='*.xic.xaml;xic.axd;*.xaml.???;*.svg.???;*.psd.???;*.svgz.???;*.xaml.ps;*.svg.ps;*.psd.ps;*.svgz.ps' type='XamlImageConverter.Web.XamlImageConverter, XamlImageConverter.Web, Version=3.5.0.0, Culture=neutral, PublicKeyToken=60c2ec984bc1bb45' />"));
			*/

			File.Copy(conf, conf + ".backup");
			webconfig.Save(conf);

		}
	}
}
