using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace vc2clangdb
{
	class Program
	{
		// global ftw :)
		static string g_projDir;

		static string transformCommandLine(string vcCommandLine, string filename, string[] globalIncludes)
		{
			string clangCommandLine = "clang++ ";

			vcCommandLine = vcCommandLine.Trim();
			System.Diagnostics.Debug.Assert(vcCommandLine.EndsWith(filename));
			vcCommandLine = vcCommandLine.Remove(vcCommandLine.Length - filename.Length);

			// we can't just split by space because e.g. "/D _WIN32" must become one option. mergeOptions() handles that
			string[] tokens = vcCommandLine.Split(new char[] { ' ' });
			IEnumerable<string> options = mergeOptions(tokens.Take(tokens.Length - 1));

			foreach (string globalInclude in globalIncludes)
				clangCommandLine += "-I" + globalInclude + " ";

			foreach (string option in options)
			{
				System.Diagnostics.Debug.Assert(option.StartsWith("/"));
				string translatedOption = handleOption(option.Substring(1));
				if(!string.IsNullOrWhiteSpace(translatedOption))
					clangCommandLine += translatedOption + " ";
			}

			// microsoft compatibility options
			clangCommandLine += " -fms-compatibility -fdelayed-template-parsing -fms-extensions";

			clangCommandLine += " " + fixPath(filename);

			return clangCommandLine;
		}

		// this is still not very robust. we assume that any non-slash token belongs to the previous slash-token
		static IEnumerable<string> mergeOptions(IEnumerable<string> tokens)
		{
			string option = null;
			foreach(string token in tokens)
			{
				if (token.StartsWith("/"))
				{
					if (!String.IsNullOrEmpty(option))
						yield return option;
					option = token;
				}
				else if (!String.IsNullOrWhiteSpace(option))
					option += " " + token;
			}
			if(!String.IsNullOrWhiteSpace(option))
				yield return option;
		}

		static string handleOption(string clOption)
		{
			if (clOption.StartsWith("D"))
				return "-" + clOption;
			else if (clOption.StartsWith("I"))
				// make sure that the path is not enclosed with "". VS is ok with that, but clang is not.
				return "-I" + fixPath(clOption.Substring(1).Trim().Trim(new char[] { '"' }));
			else if (clOption.StartsWith("Yu"))
			{
				// Visual Studio magically knows where the pch include is, but clang doesn't, so let's help by searching
				var pchName = clOption.Substring(2).Trim().Trim(new char[] { '"' });
				string[] pchLocation = Directory.GetFiles(g_projDir, pchName, SearchOption.AllDirectories);
				string locationToUse = null;
				if (pchLocation.Length == 0)
					Console.WriteLine("WARNING: pch file '" + pchName + "' doesn't exist!");
				else
				{
					if (pchLocation.Length > 1)
					{
						Console.WriteLine("WARNING: more than one instance of pch '" + pchName + "' was found!");
						foreach (string location in pchLocation)
						{
							string locationWithoutFilename = Path.GetDirectoryName(location);
							if (locationWithoutFilename == g_projDir)
							{
								locationToUse = locationWithoutFilename;
								Console.WriteLine("Assuming default location.");
							}
						}
					}

					if (locationToUse == null)
						locationToUse = Path.GetDirectoryName(pchLocation[0]);

					return "-I" + fixPath(locationToUse);
				}
			}
			return "";
		}

		static string fixPath(string path)
		{
			return path.Replace('\\', '/');
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
				Console.WriteLine("-o <output-json>     - Target path for the output json. If not specified, compile_commands.json will be created in the same location as the .vcxproj");
				Console.WriteLine("-c <configuration>   - Configuration name, e.g. 'Debug'. Currently only needed if intermediates do not exist. If not specified, msbuild will use the default");
				Console.WriteLine("-p <platform>        - Platform name, e.g. 'Win32'. Currently only needed if intermediates do not exist. If not specified, msbuild will use the default");
				Console.WriteLine("-I <global-includes> - A list of global includes, separated by ? (question mark)");
				return;
			}
			if(!File.Exists(args[0]))
			{
				Console.WriteLine(args[0] + " does not exist!");
				return;
			}

			string proj = argDict[""];
			g_projDir = fixPath(Path.GetDirectoryName(proj));
			string tlogPath = Path.Combine(argDict["i"], Path.GetFileNameWithoutExtension(proj) + ".tlog\\cl.command.1.tlog");
			string target = argDict.ContainsKey("o") ? argDict["o"] : (fixPath(Path.Combine(g_projDir, "compile_commands.json")));
			string intermediate = argDict["i"].TrimEnd(new char[] { '/', '\\' }) + "\\"; // make sure theres exactly one backslash
			string[] globalIncludes = argDict.ContainsKey("I") ? argDict["I"].Split(new char[]{'?'}) : new string[0];

			if (!File.Exists(tlogPath) || File.GetLastWriteTimeUtc(tlogPath) < File.GetLastWriteTimeUtc(proj))
			{
				var vs = VisualStudio.Find(12); // TODO: extract version from vcxproj
				vs.Build(proj, argDict.ContainsKey("p") ? argDict["p"] : null, argDict.ContainsKey("c") ? argDict["c"] : null, intermediate);
				if(!File.Exists(tlogPath))
					vs.Rebuild(proj, argDict.ContainsKey("p") ? argDict["p"] : null, argDict.ContainsKey("c") ? argDict["c"] : null, intermediate);
			}

			string[] lines = File.ReadAllLines(tlogPath);
			int lineIndex = 0;
			// the JSON format is so simple, we rather write is as text than dealing with serialization stuff
			string compilationDatabase = "[";
			for(int i = 0; i < lines.Length / 2; ++i, ++lineIndex)
			{
				string filename = lines[lineIndex++].Substring(1);
				string clangCommandline = transformCommandLine(lines[lineIndex], filename, globalIncludes);

				compilationDatabase += "\n\t{\n\t\t";
				compilationDatabase += "\"directory\": \"" + g_projDir + "\",\n\t\t";
				compilationDatabase += "\"command\": \"" + clangCommandline + "\",\n\t\t";
				// TODO: relative filename
				compilationDatabase += "\"file\": \"" + fixPath(filename) + "\"\n\t}";
				if(lines.Length - lineIndex > 2)
					compilationDatabase += ",";
			}
			compilationDatabase += "\n]";

			File.WriteAllText(target, compilationDatabase);
		}
	}
}
