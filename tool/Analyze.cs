using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CCFree
{
	public static class Analyze
	{
		public static readonly string[] CcFiles = { "ccbgssse001-fish.esm", "ccqdrsse001-survivalmode.esl", "ccbgssse037-curios.esl", "ccbgssse025-advdsgs.esm", "_ResourcePack.esl" };

		public static bool IsCc(string file) => CcFiles.Any(c => string.Equals(c, file, StringComparison.OrdinalIgnoreCase));

		static Dictionary<string, Plugin> cc = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
		static Plugin ussep;
		static List<Plugin> vanilla = new List<Plugin>();

		static Record CcRecord(string file, uint local)
		{
			Plugin p = cc[file];
			int selfIdx = p.Masters.Count;
			Record r;
			p.ByFormId.TryGetValue(((uint)selfIdx << 24) | local, out r);
			return r;
		}

		static string Describe(uint fid)
		{
			string file = ussep.FileOf(fid);
			uint local = fid & 0xFFFFFF;
			if (IsCc(file))
			{
				bool inUssep = ussep.ByFormId.ContainsKey(fid);
				Record r = CcRecord(file, local);
				return string.Format("{0}:{1:X6} {2} {3}{4}", file, local, r?.Type ?? "????", r?.EditorId() ?? "", inUssep ? " (USSEP defines)" : " MISSING");
			}
			Record v = Vanilla(fid);
			return string.Format("{0:X8} {1} {2}", fid, v?.Type ?? "", v?.EditorId() ?? "");
		}

		public static Record Vanilla(uint fid)
		{
			string file = ussep.FileOf(fid);
			Record found = null;
			foreach (var p in vanilla)
			{
				int idx = p.Masters.FindIndex(m => string.Equals(m, file, StringComparison.OrdinalIgnoreCase));
				if (string.Equals(p.Name, file, StringComparison.OrdinalIgnoreCase))
					idx = p.Masters.Count;
				if (idx < 0)
					continue;
				Record r;
				if (p.ByFormId.TryGetValue(((uint)idx << 24) | (fid & 0xFFFFFF), out r))
					found = r;
			}
			return found;
		}

		static bool Missing(uint fid)
		{
			if (fid == 0 || (fid >> 24) >= ussep.Masters.Count)
				return false;
			return IsCc(ussep.FileOf(fid)) && !ussep.ByFormId.ContainsKey(fid);
		}

		static bool CcForm(uint fid) => fid != 0 && (fid >> 24) < ussep.Masters.Count && IsCc(ussep.FileOf(fid));

		static readonly string[] Ops = { "==", "!=", ">", ">=", "<", "<=", "?6", "?7" };

		static string Ctda(byte[] d)
		{
			int op = d[0] >> 5;
			int fl = d[0] & 0x1F;
			bool glob = (fl & 0x04) != 0;
			uint comp = BitConverter.ToUInt32(d, 4);
			string compS = glob ? "GLOB[" + Describe(comp) + "]" : BitConverter.ToSingle(d, 4).ToString();
			int func = BitConverter.ToUInt16(d, 8);
			uint p1 = BitConverter.ToUInt32(d, 12);
			uint p2 = BitConverter.ToUInt32(d, 16);
			uint runOn = BitConverter.ToUInt32(d, 20);
			return string.Format("func={0} p1=[{1}] p2=[{2}] {3} {4} flags={5:X2}{6} runOn={7}", func, Describe(p1), Describe(p2), Ops[op], compS, fl, (fl & 1) != 0 ? " OR" : "", runOn);
		}

		public const string UssepName = "unofficial skyrim special edition patch.esp";
		public static readonly string[] VanillaFiles = { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };

		// Needs a profile that still has the original CC plugins, for their editor IDs.
		public static void Run(LoadOrder lo, string outPath)
		{
			ussep = lo.Load(UssepName);
			foreach (var f in CcFiles)
				cc[f] = lo.Load(f);
			foreach (var f in VanillaFiles)
				vanilla.Add(lo.Load(f));

			var sb = new StringBuilder();
			sb.AppendLine("USSEP masters: " + string.Join(", ", ussep.Masters));
			foreach (var r in ussep.ByFormId.Values)
			{
				bool ccRec = CcForm(r.FormId);
				var lines = new List<string>();
				List<Sub> subs = r.Subs();
				foreach (var s in subs)
				{
					switch (s.Type)
					{
					case "CTDA":
						{
							uint comp = BitConverter.ToUInt32(s.Data, 4);
							bool glob = (s.Data[0] & 0x04) != 0;
							uint p1 = BitConverter.ToUInt32(s.Data, 12), p2 = BitConverter.ToUInt32(s.Data, 16);
							if ((glob && CcForm(comp)) || CcForm(p1) || CcForm(p2))
								lines.Add("  CTDA " + Ctda(s.Data));
							break;
						}
					case "NVNM":
					case "NVMI":
					case "MHDT":
					case "PSDT":
					case "VMAD":
					case "OFST":
					case "VHGT":
					case "VNML":
					case "VCLR":
					case "BTXT":
					case "ATXT":
					case "VTXT":
					case "XCLW":
					case "DATA" when r.Type == "LAND":
						if (s.Type == "VMAD")
							foreach (var v in Vmad.Objects(s.Data))
								if (CcForm(v.Value))
									lines.Add("  VMAD " + v.Key + " -> " + Describe(v.Value));
						break;
					default:
						for (int i = 0; i + 4 <= s.Data.Length; i += 4)
						{
							uint v = BitConverter.ToUInt32(s.Data, i);
							if (CcForm(v) && (v & 0xFFFFFF) >= 0x800 || (CcForm(v) && ussep.FileOf(v) == "_ResourcePack.esl"))
								lines.Add(string.Format("  {0}+{1} -> {2}", s.Type, i, Describe(v)));
						}
						break;
					}
				}
				if (lines.Count == 0)
					continue;
				Record van = ccRec ? null : Vanilla(r.FormId);
				sb.AppendLine(string.Format("{0} {1:X8} {2} {3} flags={4:X}", ccRec ? "CC" : (van == null ? "USSEP-NEW" : "VANILLA"), r.FormId, r.Type, r.EditorId(), r.Flags));
				foreach (var l in lines)
					sb.AppendLine(l);
			}
			File.WriteAllText(outPath, sb.ToString());
		}
	}
}
