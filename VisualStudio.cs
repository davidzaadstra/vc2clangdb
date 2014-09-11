using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;

namespace vc2clangdb
{
	public class VisualStudio
	{
		// This is not at all robust yet! It was tested on my own express machine only. The paths suggested online
		// do not exist, so I searched the registry myself. Major versions other than 12 might have completely different
		// paths.
		public static VisualStudio Find(int majorVersion)
		{
			string path = null;
			foreach (bool express in new bool[] { false, true })
			{
				string registryKeyString = String.Format(@"SOFTWARE\{0}\Microsoft\{1}\{2}.0{3}\Setup\VS",
				   Environment.Is64BitProcess ? @"Wow6432Node" : "",
				   express ? "WDExpress" : "VisualStudio",
				   majorVersion,
				   express ? "" : "_Config");

				// TODO: I haven't done any testing, but I guess that LocalMachine vs CurrentUser depends on whether the user chose "Install for this user" or "Install for all users" during installation?
				using (RegistryKey localMachineKey = (express ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(registryKeyString))
				{
					path = localMachineKey.GetValue("ProductDir") as string;
				}
				if (!String.IsNullOrEmpty(path))
					break;
			}
			return new VisualStudio(path, majorVersion);
		}

		public string PreprocessProject(string projectPath)
		{
			var msbuild = new Process();
			msbuild.StartInfo.Arguments = "/property:PlatformName=x64 /preprocess " + projectPath;
			msbuild.StartInfo.FileName = GetMSBuild();
			msbuild.StartInfo.RedirectStandardOutput = true;
			msbuild.StartInfo.UseShellExecute = false;
			msbuild.Start();
			string output = msbuild.StandardOutput.ReadToEnd();

			// now we could parse the project, but that's complicated. we need to evaluate expressions etc
			// also, we need to ask the user for Configuration and Platform

			throw new NotImplementedException();
		}

		public void Build(string projectPath, string platform, string configuration, string intermediateDir)
		{
			RunMSBuild(projectPath, BuildPropString(platform, configuration, intermediateDir));
		}

		public void Rebuild(string projectPath, string platform, string configuration, string intermediateDir)
		{
			Console.WriteLine(RunMSBuild(projectPath, "/target:Rebuild" + BuildPropString(platform, configuration, intermediateDir)));
		}

		private VisualStudio(string path, int majorVersion)
		{
			m_path = path;
			m_majorVersion = majorVersion;
		}

		private string RunMSBuild(string projectPath, string args)
		{
			var msbuild = new Process();
			msbuild.StartInfo.WorkingDirectory = Path.GetDirectoryName(GetMSBuild());
			msbuild.StartInfo.FileName = GetMSBuild();
			msbuild.StartInfo.Arguments = projectPath + " " + args;
			msbuild.StartInfo.RedirectStandardOutput = true;
			msbuild.StartInfo.UseShellExecute = false;
			msbuild.Start();
			return msbuild.StandardOutput.ReadToEnd();
		}

		private string GetMSBuild()
		{
			return Path.Combine(m_path, String.Format("../msbuild/{0}.0/bin/msbuild.exe", m_majorVersion));
		}

		private static string BuildPropString(string platform, string configuration, string intermediateDir)
		{
			var props = new List<string>();
			if (!String.IsNullOrEmpty(platform))
				props.Add("PlatformName=" + platform);
			if (!String.IsNullOrEmpty(configuration))
				props.Add("Configuration=" + configuration);
			if (!String.IsNullOrEmpty(intermediateDir))
				props.Add("IntDir=" + intermediateDir);
			string propArg = "";
			if (props.Count > 0)
				propArg = " /property:" + String.Join(";", props);

			return propArg;
		}

		private string m_path;
		private int m_majorVersion;
	}
}
