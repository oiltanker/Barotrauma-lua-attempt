using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using Microsoft.CodeAnalysis;
using System.Runtime.Loader;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma
{
	class CsScriptLoader : CsScriptBase
	{
		private LuaCsSetup setup;
		private List<MetadataReference> defaultReferences;

		private Dictionary<string, List<string>> sources;
		public Assembly Assembly { get; private set; }

		public CsScriptLoader(LuaCsSetup setup)
		{
			this.setup = setup;

			defaultReferences = AppDomain.CurrentDomain.GetAssemblies()
				.Where(a => !(a.IsDynamic || string.IsNullOrEmpty(a.Location) || a.Location.Contains("xunit")))
				.Select(a => MetadataReference.CreateFromFile(a.Location) as MetadataReference)
				.ToList();

			sources = new Dictionary<string, List<string>>();
			Assembly = null;
		}

		private enum RunType { Standard, Forced, None };
		private bool ShouldRun(ContentPackage cp, string path)
		{
			if (!Directory.Exists(path + "CSharp")) return false;

			var isEnabled = ContentPackageManager.EnabledPackages.All.Contains(cp);
			if (File.Exists(path + "CSharp/RunConfig.xml"))
			{
				var doc = XDocument.Load(File.Open(path + "CSharp/RunConfig.xml", FileMode.Open, FileAccess.Read));
				var elems = doc.Root.Elements().ToArray();
				var elem = elems.FirstOrDefault(e => e.Name.LocalName.Equals(LuaCsSetup.IsServer ? "Server" : (LuaCsSetup.IsClient ? "Client" : "None"), StringComparison.OrdinalIgnoreCase));

				if (elem != null && Enum.TryParse(elem.Value, true, out RunType rtValue))
				{
					if (rtValue == RunType.Standard && isEnabled)
					{
						LuaCsSetup.PrintCsMessage($"Standard run C# of {cp.Name}");
						return true;
					}
					else if (rtValue == RunType.Forced)
					{
						LuaCsSetup.PrintCsMessage($"Forced run C# of {cp.Name}");
						return true;
					}
					else if (rtValue == RunType.None) return false;
				}
			}

			if (isEnabled)
			{
				LuaCsSetup.PrintCsMessage($"Assumed run C# of {cp.Name}");
				return true;
			}
			else return false;
		}
		public void SearchFolders()
        {
			var paths = new Dictionary<string, string>();
			foreach (var cp in ContentPackageManager.AllPackages)
            {
				var path = $"{Path.GetFullPath(Path.GetDirectoryName(cp.Path)).Replace('\\','/')}/";
				if (ShouldRun(cp, path))
				{
					if (paths.ContainsKey(cp.Name))
                    {
						if (ContentPackageManager.EnabledPackages.All.Contains(cp)) paths[cp.Name] = path;
					}
					else paths.Add(cp.Name, path);
				}
			}
			foreach ((var _, var path) in paths) RunFolder(path);
		}

		public bool HasSources { get => sources.Count > 0; }

		private enum SourceCategory { Shared, Server, Client };
		private Regex rMaskPathValid = new Regex(@"^/?(((?!csharp)[^/])+/)+csharp(/(shared|client|server))?(/[^/]+)+\.cs$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private Regex rMaskPathCategory1 = new Regex(@"/(shared|client|server)(/[^/]+)+\.cs$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private Regex rMaskPathCategory2 = new Regex(@"^/(shared|client|server)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private void RunFolder(string folder)
		{
			var offendingSources = new List<string>();
			foreach (var str in DirSearch(folder))
			{
				var s = str.Replace("\\", "/");

				if (s.EndsWith(".cs") && LuaCsFile.IsPathAllowedCsException(s))
				{
					if (rMaskPathValid.IsMatch(s)) // valid path
					{
						var sourceCategory = SourceCategory.Shared;
						{
							var match = rMaskPathCategory1.Match(s);
							if (match.Success) match = rMaskPathCategory2.Match(match.Value);
							if (match.Success)
							{
								if (match.Value.EndsWith("shared", StringComparison.OrdinalIgnoreCase)) sourceCategory = SourceCategory.Shared;
								else if (match.Value.EndsWith("server", StringComparison.OrdinalIgnoreCase)) sourceCategory = SourceCategory.Server;
								else if (match.Value.EndsWith("client", StringComparison.OrdinalIgnoreCase)) sourceCategory = SourceCategory.Client;
							}
						}

						var belongsInAssembly = false;
						{
							if (sourceCategory == SourceCategory.Shared) belongsInAssembly = true;
							else if (sourceCategory == SourceCategory.Server && LuaCsSetup.IsServer) belongsInAssembly = true;
							else if (sourceCategory == SourceCategory.Client && LuaCsSetup.IsClient) belongsInAssembly = true;
						}

						if (belongsInAssembly)
						{
							if (sources.ContainsKey(folder)) sources[folder].Add(s);
							else sources.Add(folder, new List<string> { s });
						}
					}
					else offendingSources.Add(s);
				}
			}
			if (offendingSources.Count > 0)
            {
				LuaCsSetup.PrintCsError($"All C# sources must belong to <mod_folder>/CSharp/*\n  Offending sources:{offendingSources.Select(s => $"\n    {s}").Aggregate((s1, s2) => $"{s1}{s2}")}");
            }
		}

		private IEnumerable<SyntaxTree> ParseSources() {
			var syntaxTrees = new List<SyntaxTree>();

			if (sources.Count <= 0) throw new Exception("No Cs sources detected");
			syntaxTrees.Add(AssemblyInfoSyntaxTree(NET_SCRIPT_ASSEMBLY));
			foreach ((var folder, var src) in sources)
            {
				try
				{
					foreach (var file in src)
					{
						var tree = SyntaxFactory.ParseSyntaxTree(File.ReadAllText(file), ParseOptions, file);
						var error = CsScriptFilter.FilterSyntaxTree(tree as CSharpSyntaxTree); // Check file content for prohibited stuff
						if (error != null) throw new Exception(error);

						syntaxTrees.Add(tree);
					}
				}
				catch (Exception ex)
				{
					LuaCsSetup.PrintCsError("Error loading '" + folder + "':\n" + ex.Message + "\n" + ex.StackTrace);
				}
			}

			return syntaxTrees;
		}

        public List<Type> Compile()
        {
			IEnumerable<SyntaxTree> syntaxTrees = ParseSources();

			var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				.WithMetadataImportOptions(MetadataImportOptions.All)
				.WithOptimizationLevel(OptimizationLevel.Release)
				.WithAllowUnsafe(false);
			var compilation = CSharpCompilation.Create(NET_SCRIPT_ASSEMBLY,syntaxTrees, defaultReferences, options);

			using (var mem = new MemoryStream())
			{
				var result = compilation.Emit(mem);
				if (!result.Success)
				{
					IEnumerable<Diagnostic> failures = result.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);

					string errStr = "NET MODS NOT LOADED | Mod compilation errors:";
					foreach (Diagnostic diagnostic in failures)
						errStr += $"\n{diagnostic}";
					LuaCsSetup.PrintCsError(errStr);
				}
				else
				{
					mem.Seek(0, SeekOrigin.Begin);
					var errStr = CsScriptFilter.FilterMetadata(new PEReader(mem).GetMetadataReader());
					if (errStr == null)
                    {
						mem.Seek(0, SeekOrigin.Begin);
						Assembly = LoadFromStream(mem);
					}
					else LuaCsSetup.PrintCsError(errStr);
				}
			}

			if (Assembly != null)
				return Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ACsMod))).ToList();
			else
				throw new Exception("Unable to create net mods assembly.");
		}

		private static string[] DirSearch(string sDir)
		{
			return Directory.GetFiles(sDir, "*.cs", SearchOption.AllDirectories);
		}

		public void Clear()
        {
			Assembly = null;
        }
	}
}