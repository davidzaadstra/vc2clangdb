using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace vc2clangdb
{
	class Program
	{
		static string handleOption(string clOption)
		{
			if (clOption.StartsWith("D"))
				return "-" + clOption;
			else if (clOption.StartsWith("I"))
				return "-" + clOption;
			return "";
		}

		static string fixPath(string path)
		{
			return path.Replace('\\', '/');
		}

		static string escapeString(string str)
		{
			return fixPath(str);
		}

		static Dictionary<string, string> parseArgs(string[] args)
		{
			var dict = new Dictionary<string, string>();

			try
			{
				for (int i = 0; i < args.Length; ++i)
				{
					if (args[i].StartsWith("-"))
					{
						dict.Add(args[i].Substring(1), args[i + 1]);
						++i;
					}
					else
						dict.Add("", args[i]);
				}
			}
			catch(Exception)
			{
				Console.WriteLine("Error parsing arguments...");
			}
			return dict;
		}

		static void Main(string[] args)
		{
			var argDict = parseArgs(args);
			if (!argDict.ContainsKey("") || !argDict.ContainsKey("i"))
			{
				Console.WriteLine("Usage: vc2clangdb <path-to-vcxproj> -i <intermediate-path> [options]\n");
				Console.WriteLine("Options:");
				Console.WriteLine("-o <output-json>   - Target path for the output json. If not specified, compile_commands.json will be created in the same location as the .vcxproj");
				Console.WriteLine("-c <configuration> - Configuration name, e.g. 'Debug'. Currently only needed if intermediates do not exist. If not specified, msbuild will use the default");
				Console.WriteLine("-p <platform>      - Platform name, e.g. 'Win32'. Currently only needed if intermediates do not exist. If not specified, msbuild will use the default");
				return;
			}
			if(!File.Exists(args[0]))
			{
				Console.WriteLine(args[0] + " does not exist!");
				return;
			}

			string proj = argDict[""];
			string projDir = fixPath(Path.GetDirectoryName(proj));
			string tlogPath = Path.Combine(argDict["i"], Path.GetFileNameWithoutExtension(proj) + ".tlog\\cl.command.1.tlog");
			string target = argDict.ContainsKey("o") ? argDict["o"] : (fixPath(Path.Combine(projDir, "compile_commands.json")));

			if (!File.Exists(tlogPath) || File.GetLastWriteTimeUtc(tlogPath) < File.GetLastWriteTimeUtc(proj))
			{
				var vs = VisualStudio.Find(12); // TODO: extract version from vcxproj
				vs.Build(proj, argDict.ContainsKey("p") ? argDict["p"] : null, argDict.ContainsKey("c") ? argDict["c"] : null, argDict["i"]);
				if(!File.Exists(tlogPath))
					vs.Rebuild(proj, argDict.ContainsKey("p") ? argDict["p"] : null, argDict.ContainsKey("c") ? argDict["c"] : null, argDict["i"]);
			}

			string[] lines = File.ReadAllLines(tlogPath);
			int index = 0;
			string compilationDatabase = "[";
			for(int i = 0; i < lines.Length / 2; ++i, ++index)
			{
				string clangCommandline = "clang++ ";

				string filename = fixPath(lines[index++].Substring(1));
				string commandline = lines[index];

				// this is not robust. if we encounter a / within a string, we might run into problems
				string[] options = commandline.Split(new char[] { '/' });
				foreach (string option in options)
					clangCommandline += escapeString(handleOption(option));

				clangCommandline += " -fms-compatibility";
				clangCommandline += " " + filename;
				
				compilationDatabase += "\n\t{\n\t\t";
				compilationDatabase += "\"directory\": \"" + projDir + "\",\n\t\t";
				compilationDatabase += "\"command\": \"" + clangCommandline + "\",\n\t\t";
				// TODO: relative filename
				compilationDatabase += "\"file\": \"" + filename + "\"\n\t}";
				if(lines.Length - index > 2)
					compilationDatabase += ",";
			}
			compilationDatabase += "\n]";

			File.WriteAllText(target, compilationDatabase);
		}
	}
}
