using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCFree
{
	public static class Build
	{
		public const string CleanupName = "USSEP CC Cleanup.esp";
		const string Author = "hankinsrim";
		const uint SurvivalModeEnabled = 0x06000826;
		const uint VanillaFishingRod = 0x000E8B39;
		const uint InertMarker = 0x0000003B;
		const uint InertActor = 0x0006DC9D;

		static readonly uint[] DisableRefs = { 0x0505C40A, 0x0505C56D, 0x080B446F, 0x080B4470, 0x080B4471, 0x050F9AB1, 0x080AF353, 0x050E5496, 0x08001035, 0x0800102E, 0x0800103C, 0x08001020, 0x081B6924, 0x0800097D, 0x0800105C, 0x08000901, 0x08001065, 0x08001064, 0x080008FB, 0x05000988 };
		static readonly uint[] StartQuestsToClear = { 0x080010BF };

		static Plugin U;
		static Plugin Sky;
		public static List<string> Log = new List<string>();

		static bool CcSpace(uint fid) => fid != 0 && (fid >> 24) < U.Masters.Count && Analyze.IsCc(U.FileOf(fid));
		static bool Missing(uint fid) => CcSpace(fid) && !U.ByFormId.ContainsKey(fid);
		static uint U32(byte[] d, int o) => BitConverter.ToUInt32(d, o);

		public static void Run(LoadOrder lo, string outDir)
		{
			U = lo.Load(Analyze.UssepName);
			Sky = lo.Load("Skyrim.esm");

			string dummyDir = Path.Combine(outDir, "CC Dummy Masters");
			Directory.CreateDirectory(dummyDir);
			foreach (var f in Analyze.CcFiles)
			{
				uint flags = f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase) ? 0x201u : 0x1u;
				File.WriteAllBytes(Path.Combine(dummyDir, f), Writer.Header(flags, new string[0], 0, Author, ""));
			}

			var tree = new Tree(U);
			foreach (var fid in DisableRefs)
			{
				Record r = U.ByFormId[fid];
				var o = tree.Add(r);
				o.Flags |= 0x800;
				o.Subs = r.Subs().Where(s => s.Type != "XESP" && !(s.Type == "XOWN" && Missing(U32(s.Data, 0))) && !(s.Type == "VMAD" && Vmad.Objects(s.Data).Any(t => Missing(t.Value)))).ToList();
				Log.Add(string.Format("disable {0:X8} {1}", fid, r.Type));
			}
			// CC references whose base object is CC: give them an inert vanilla base, since the engine still creates them.
			foreach (var r in U.ByFormId.Values.ToList())
			{
				if ((r.Type != "REFR" && r.Type != "ACHR") || !CcSpace(r.FormId) || DisableRefs.Contains(r.FormId))
					continue;
				var subs = r.Subs();
				var name = subs.FirstOrDefault(s => s.Type == "NAME");
				if (name == null || !CcSpace(U32(name.Data, 0)))
					continue;
				uint inert = r.Type == "ACHR" ? InertActor : InertMarker;
				var o = tree.Add(r);
				o.Flags |= 0x800;
				o.Subs = subs.Where(s => s.Type == "EDID" || s.Type == "DATA").ToList();
				o.Subs.Insert(o.Subs.FindIndex(s => s.Type == "DATA"), new Sub("NAME", BitConverter.GetBytes(inert)));
				Log.Add(string.Format("neutralize {0} {1:X8} {2} base {3:X8} -> {4:X8}", r.Type, r.FormId, r.EditorId(), U32(name.Data, 0), inert));
			}
			foreach (var fid in StartQuestsToClear)
			{
				Record r = U.ByFormId[fid];
				var o = tree.Add(r);
				o.Subs = r.Subs();
				var dnam = o.Subs.First(s => s.Type == "DNAM");
				dnam.Data[0] &= 0xFE;
				Log.Add(string.Format("clear start game enabled {0:X8} {1}", fid, r.EditorId()));
			}
			foreach (var r in U.ByFormId.Values.ToList())
			{
				if (CcSpace(r.FormId) || (r.FormId >> 24) >= 5)
					continue;
				List<Sub> subs = r.Subs();
				List<Sub> fixedSubs = Fix(r, subs);
				if (fixedSubs == null)
					continue;
				var o = tree.Add(r);
				o.Subs = fixedSubs;
				foreach (var left in Leftovers(r, fixedSubs))
					Log.Add(string.Format("  LEFTOVER {0:X8} {1}: {2}", r.FormId, r.EditorId(), left));
			}

			var masters = U.Masters.Concat(new[] { U.Name }).ToList();
			string cleanDir = Path.Combine(outDir, "USSEP CC Cleanup");
			Directory.CreateDirectory(cleanDir);
			using (var fs = File.Create(Path.Combine(cleanDir, CleanupName)))
			{
				var w = new BinaryWriter(fs);
				w.Write(Writer.Header(0x200, masters, Writer.Count(tree.Top), Author, "Removes leftovers of absent free Creation Club content from USSEP."));
				foreach (var g in tree.Top)
					Writer.WriteGroup(w, (OutGroup)g);
			}
			File.WriteAllLines(Path.Combine(outDir, "build-log.txt"), Log);
		}

		// Returns the cleaned subrecords, or null when the record has nothing to clean.
		static List<Sub> Fix(Record r, List<Sub> subs)
		{
			var changes = new List<string>();
			var result = new List<Sub>();

			if (r.Type == "REFR" && subs.Any(s => s.Type == "NAME" && CcSpace(U32(s.Data, 0))))
			{
				uint vanillaBase = 0;
				Record v;
				if (Sky.ByFormId.TryGetValue(r.FormId, out v))
					vanillaBase = U32(v.Subs().First(s => s.Type == "NAME").Data, 0);
				if (vanillaBase != VanillaFishingRod)
					throw new Exception(string.Format("unexpected REFR {0:X8} with CC base", r.FormId));
				foreach (var s in subs)
					result.Add(s.Type == "NAME" ? new Sub("NAME", BitConverter.GetBytes(vanillaBase)) : s);
				Log.Add(string.Format("restore base {0:X8} -> {1:X8}", r.FormId, vanillaBase));
				return result;
			}

			bool hasEffects = subs.Any(s => s.Type == "EFID") && r.Type != "MGEF";
			int i = 0;
			// Prefix (all subrecords for records without effect lists).
			while (i < subs.Count && !(hasEffects && subs[i].Type == "EFID"))
			{
				if (subs[i].Type == "CTDA")
				{
					var run = TakeConditions(subs, ref i);
					bool alwaysFalse;
					var simplified = Simplify(run, out alwaysFalse, changes);
					if (alwaysFalse)
						throw new Exception(string.Format("{0:X8} {1} record conditions become always false", r.FormId, r.EditorId()));
					result.AddRange(simplified.SelectMany(c => c));
					continue;
				}
				Sub s = FixSub(r, subs[i++], changes);
				if (s != null)
					result.Add(s);
			}
			// Effect entries: EFID, EFIT, conditions.
			while (i < subs.Count)
			{
				var effect = new List<Sub> { subs[i++] };
				while (i < subs.Count && subs[i].Type != "EFID")
					effect.Add(subs[i++]);
				uint mgef = U32(effect[0].Data, 0);
				if (Missing(mgef))
				{
					changes.Add(string.Format("drop effect with missing MGEF {0:X8}", mgef));
					continue;
				}
				int j = 0;
				var kept = new List<Sub>();
				bool drop = false;
				while (j < effect.Count)
				{
					if (effect[j].Type == "CTDA")
					{
						var run = TakeConditions(effect, ref j);
						bool alwaysFalse;
						var simplified = Simplify(run, out alwaysFalse, changes);
						if (alwaysFalse)
						{
							drop = true;
							break;
						}
						kept.AddRange(simplified.SelectMany(c => c));
						continue;
					}
					kept.Add(effect[j++]);
				}
				if (drop)
				{
					changes.Add(string.Format("drop effect {0:X8} whose conditions are always false without CC", mgef));
					continue;
				}
				result.AddRange(kept);
			}

			if (changes.Count == 0)
				return null;
			var kwda = result.FirstOrDefault(s => s.Type == "KWDA");
			int ksiz = result.FindIndex(s => s.Type == "KSIZ");
			if (kwda != null && ksiz >= 0)
				result[ksiz] = new Sub("KSIZ", BitConverter.GetBytes((uint)(kwda.Data.Length / 4)));
			Log.Add(string.Format("clean {0} {1:X8} {2}", r.Type, r.FormId, r.EditorId()));
			foreach (var c in changes)
				Log.Add("  " + c);
			return result;
		}

		static readonly Dictionary<string, int[]> LocationArrays = new Dictionary<string, int[]>
		{
			{ "ACPR", new[] { 12, 0, 4 } }, { "LCPR", new[] { 12, 0, 4 } },
			{ "ACUN", new[] { 12, 0, 4, 8 } }, { "LCUN", new[] { 12, 0, 4, 8 } },
			{ "ACSR", new[] { 16, 0, 4, 8 } }, { "LCSR", new[] { 16, 0, 4, 8 } },
			{ "ACEP", new[] { 12, 0, 4 } }, { "LCEP", new[] { 12, 0, 4 } },
			{ "ACID", new[] { 4, 0 } }, { "LCID", new[] { 4, 0 } },
		};

		static Sub FixSub(Record r, Sub s, List<string> changes)
		{
			if (s.Type == "KWDA")
			{
				var keep = new List<uint>();
				for (int o = 0; o + 4 <= s.Data.Length; o += 4)
				{
					uint k = U32(s.Data, o);
					if (Missing(k))
						changes.Add(string.Format("drop keyword {0:X8}", k));
					else
						keep.Add(k);
				}
				if (keep.Count * 4 == s.Data.Length)
					return s;
				return new Sub("KWDA", keep.SelectMany(BitConverter.GetBytes).ToArray());
			}
			if (s.Type == "XNAM" && r.Type == "FACT" && Missing(U32(s.Data, 0)))
			{
				changes.Add(string.Format("drop relation to {0:X8}", U32(s.Data, 0)));
				return null;
			}
			int[] layout;
			if (r.Type == "LCTN" && LocationArrays.TryGetValue(s.Type, out layout))
			{
				int size = layout[0];
				var keep = new List<byte>();
				for (int o = 0; o + size <= s.Data.Length; o += size)
				{
					bool cc = layout.Skip(1).Any(off => CcSpace(U32(s.Data, o + off)));
					if (cc)
						changes.Add(string.Format("drop {0} entry {1}", s.Type, BitConverter.ToString(s.Data, o, size)));
					else
						keep.AddRange(s.Data.Skip(o).Take(size));
				}
				if (keep.Count == s.Data.Length)
					return s;
				return keep.Count == 0 ? null : new Sub(s.Type, keep.ToArray());
			}
			if (s.Type == "VMAD" && r.Type == "FLOR")
			{
				var props = Vmad.Offsets(s.Data).Where(t => Missing(t.Item2)).ToList();
				if (props.Count == 0)
					return s;
				Record v = Sky.ByFormId[r.FormId];
				var vprops = Vmad.Offsets(v.Subs().First(x => x.Type == "VMAD").Data);
				var data = (byte[])s.Data.Clone();
				foreach (var p in props)
				{
					var vp = vprops.First(t => t.Item1 == p.Item1);
					BitConverter.GetBytes(vp.Item2).CopyTo(data, p.Item3);
					changes.Add(string.Format("restore {0} {1:X8} -> vanilla {2:X8}", p.Item1, p.Item2, vp.Item2));
				}
				return new Sub("VMAD", data);
			}
			return s;
		}

		static List<List<Sub>> TakeConditions(List<Sub> subs, ref int i)
		{
			var run = new List<List<Sub>>();
			while (i < subs.Count && subs[i].Type == "CTDA")
			{
				var c = new List<Sub> { subs[i++] };
				while (i < subs.Count && (subs[i].Type == "CIS1" || subs[i].Type == "CIS2"))
					c.Add(subs[i++]);
				run.Add(c);
			}
			return run;
		}

		// Value of a condition with the CC content absent, or null if it does not depend on CC.
		static bool? Eval(byte[] c)
		{
			int func = BitConverter.ToUInt16(c, 8);
			uint p1 = U32(c, 12);
			bool glob = (c[0] & 0x04) != 0;
			float value;
			if (func == 74 && p1 == SurvivalModeEnabled)
				value = 0;
			else if ((func == 214 || func == 699) && Missing(p1))
				value = 0;
			else
				return null;
			if (glob)
				return null;
			float cv = BitConverter.ToSingle(c, 4);
			switch (c[0] >> 5)
			{
			case 0: return value == cv;
			case 1: return value != cv;
			case 2: return value > cv;
			case 3: return value >= cv;
			case 4: return value < cv;
			case 5: return value <= cv;
			default: return null;
			}
		}

		static bool RefsCc(byte[] c) => Missing(U32(c, 12)) || Missing(U32(c, 16)) || ((c[0] & 0x04) != 0 && Missing(U32(c, 4))) || U32(c, 12) == SurvivalModeEnabled;

		static List<List<Sub>> Simplify(List<List<Sub>> run, out bool alwaysFalse, List<string> changes)
		{
			alwaysFalse = false;
			if (!run.Any(c => RefsCc(c[0].Data)))
				return run;
			var result = new List<List<Sub>>();
			int i = 0;
			while (i < run.Count)
			{
				var group = new List<List<Sub>>();
				while (i < run.Count)
				{
					var c = run[i++];
					group.Add(c);
					if ((c[0].Data[0] & 0x01) == 0)
						break;
				}
				var values = group.Select(c => Eval(c[0].Data)).ToList();
				if (values.Any(v => v == true))
				{
					changes.Add(string.Format("drop OR group of {0} condition(s), always true without CC", group.Count));
					continue;
				}
				var kept = group.Where((c, k) => values[k] != false).ToList();
				if (kept.Count < group.Count)
					changes.Add(string.Format("drop {0} condition(s) always false without CC", group.Count - kept.Count));
				if (kept.Count == 0)
				{
					alwaysFalse = true;
					return null;
				}
				byte lastOr = (byte)(group[group.Count - 1][0].Data[0] & 0x01);
				for (int k = 0; k < kept.Count; k++)
				{
					var copy = kept[k].Select(s => new Sub(s.Type, (byte[])s.Data.Clone())).ToList();
					if (k < kept.Count - 1)
						copy[0].Data[0] |= 0x01;
					else
						copy[0].Data[0] = (byte)((copy[0].Data[0] & 0xFE) | lastOr);
					result.Add(copy);
				}
			}
			return result;
		}

		static IEnumerable<string> Leftovers(Record r, List<Sub> subs)
		{
			foreach (var s in subs.Where(x => x != null))
			{
				if (s.Type == "CTDA" && RefsCc(s.Data))
					yield return "CTDA " + BitConverter.ToString(s.Data);
				if ((s.Type == "EFID" || s.Type == "KWDA" || s.Type == "XNAM") && Enumerable.Range(0, s.Data.Length / 4).Any(k => Missing(U32(s.Data, k * 4))))
					yield return s.Type;
				if (r.Type == "LCTN" && LocationArrays.ContainsKey(s.Type) && Enumerable.Range(0, s.Data.Length / 4).Any(k => CcSpace(U32(s.Data, k * 4))))
					yield return s.Type;
				if (s.Type == "VMAD" && Vmad.Objects(s.Data).Any(t => Missing(t.Value)))
					yield return "VMAD";
			}
		}
	}
}
