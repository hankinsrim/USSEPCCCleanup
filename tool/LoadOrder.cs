using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CCFree
{
	// Resolves plugin files through an MO2 instance's profile, falling back to the game's Data folder.
	public class LoadOrder
	{
		public readonly string Profile;
		readonly Dictionary<string, string> where = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public LoadOrder(string instance, string profile)
		{
			Profile = Path.Combine(instance, "profiles", profile);
			string ini = Path.Combine(instance, "ModOrganizer.ini");
			if (File.Exists(ini))
			{
				var m = Regex.Match(File.ReadAllText(ini), @"^gamePath=@ByteArray\((.*)\)\s*$", RegexOptions.Multiline);
				if (m.Success)
					AddDir(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "Data"));
			}
			var mods = File.ReadAllLines(Path.Combine(Profile, "modlist.txt")).Where(l => l.StartsWith("+")).Select(l => l.Substring(1)).ToList();
			for (int i = mods.Count - 1; i >= 0; i--)
				AddDir(Path.Combine(instance, "mods", mods[i]));
		}

		void AddDir(string dir)
		{
			if (!Directory.Exists(dir))
				return;
			foreach (var f in Directory.GetFiles(dir))
			{
				string ext = Path.GetExtension(f).ToLowerInvariant();
				if (ext == ".esp" || ext == ".esm" || ext == ".esl")
					where[Path.GetFileName(f)] = f;
			}
		}

		public string Find(string plugin)
		{
			string p;
			if (!where.TryGetValue(plugin, out p))
				throw new FileNotFoundException("plugin not found in profile: " + plugin);
			return p;
		}

		public Plugin Load(string plugin) => Plugin.Load(Find(plugin));

		// Plugin paths in load order; unresolved names are prefixed with "UNRESOLVED:".
		public List<string> Plugins()
		{
			var result = new List<string>();
			foreach (var l in File.ReadAllLines(Path.Combine(Profile, "loadorder.txt")))
			{
				if (l.StartsWith("#") || l.Trim() == "")
					continue;
				string p;
				result.Add(where.TryGetValue(l.Trim(), out p) ? p : "UNRESOLVED:" + l.Trim());
			}
			return result;
		}
	}
}
