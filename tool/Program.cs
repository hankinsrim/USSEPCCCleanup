using System;
using System.Collections.Generic;
using System.Linq;

namespace CCFree
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length < 3)
				return Usage();
			var lo = new LoadOrder(args[1], args[2]);
			args = new[] { args[0] }.Concat(args.Skip(3)).ToArray();
			switch (args.Length > 0 ? args[0] : "")
			{
			case "analyze":
				Analyze.Run(lo, args[1]);
				return 0;
			case "dump":
				{
					var pl = lo.Load(Analyze.UssepName);
					var sky = lo.Load("Skyrim.esm");
					foreach (var a in args.Skip(1))
					{
						uint fid = Convert.ToUInt32(a, 16);
						Record r;
						if (!pl.ByFormId.TryGetValue(fid, out r))
						{
							Console.WriteLine("{0:X8} not in USSEP", fid);
							continue;
						}
						var chain = new List<string>();
						for (var g = r.Parent; g != null; g = g.Parent)
							chain.Add(string.Format("{0}:{1:X8}", g.GroupType, g.Label));
						Console.WriteLine("{0} {1:X8} flags={2:X8} {3} groups={4}", r.Type, fid, r.Flags, r.EditorId(), string.Join(" < ", chain));
						foreach (var s in r.Subs())
						{
							string extra = "";
							if ((s.Type == "NAME" || s.Type == "XESP" || s.Type == "XLRT") && s.Data.Length >= 4)
							{
								uint b = BitConverter.ToUInt32(s.Data, 0);
								Record br;
								if ((b >> 24) == 0 && sky.ByFormId.TryGetValue(b, out br))
									extra = " " + br.Type + " " + br.EditorId();
							}
							Console.WriteLine("  {0} {1} {2}{3}", s.Type, s.Data.Length, BitConverter.ToString(s.Data, 0, Math.Min(s.Data.Length, 48)), extra);
						}
						Record v;
						if ((fid >> 24) == 0 && sky.ByFormId.TryGetValue(fid, out v))
							foreach (var s in v.Subs())
								if (s.Type == "NAME")
									Console.WriteLine("  vanilla NAME {0:X8}", BitConverter.ToUInt32(s.Data, 0));
					}
					return 0;
				}
			case "build":
				Build.Run(lo, args[1]);
				return 0;
			case "verify":
				{
					var c = Plugin.Load(args[1]);
					var u = lo.Load(Analyze.UssepName);
					var sky = lo.Load("Skyrim.esm");
					Console.WriteLine("masters: " + string.Join(", ", c.Masters) + " flags=" + c.Header.Flags.ToString("X"));
					Action<List<Node>, string> walk = null;
					walk = (nodes, indent) =>
					{
						foreach (var n in nodes)
						{
							if (n is Group g)
							{
								Console.WriteLine("{0}GRUP type={1} label={2:X8} children={3}", indent, g.GroupType, g.Label, g.Children.Count);
								walk(g.Children, indent + "  ");
							}
							else
							{
								var r = (Record)n;
								var ur = u.ByFormId[r.FormId];
								Record vr;
								sky.ByFormId.TryGetValue(r.FormId, out vr);
								Func<Record, string> eff = x => x == null ? "-" : x.Subs().Count(s => s.Type == "EFID") + "e/" + x.Subs().Count(s => s.Type == "CTDA") + "c";
								Console.WriteLine("{0}{1} {2:X8} {3} flags={4:X} same={5} ours={6} ussep={7} sky={8}", indent, r.Type, r.FormId, r.EditorId(), r.Flags, ur.Data().SequenceEqual(r.Data()), eff(r), eff(ur), eff(vr));
							}
						}
					};
					walk(c.Top, "");
					return 0;
				}
			case "conds":
				{
					var c = Plugin.Load(args[1]);
					foreach (var a in args.Skip(2))
					{
						var r = c.ByFormId[Convert.ToUInt32(a, 16)];
						Console.WriteLine("{0} {1:X8} {2}", r.Type, r.FormId, r.EditorId());
						foreach (var s in r.Subs())
						{
							if (s.Type == "EFID")
								Console.WriteLine("  EFID {0:X8}", BitConverter.ToUInt32(s.Data, 0));
							if (s.Type == "CTDA")
								Console.WriteLine("    CTDA op={0} flags={1:X2} comp={2} func={3} p1={4:X8} p2={5:X8} runOn={6}", s.Data[0] >> 5, s.Data[0] & 0x1F, (s.Data[0] & 4) != 0 ? BitConverter.ToUInt32(s.Data, 4).ToString("X8") : BitConverter.ToSingle(s.Data, 4).ToString(), BitConverter.ToUInt16(s.Data, 8), BitConverter.ToUInt32(s.Data, 12), BitConverter.ToUInt32(s.Data, 16), BitConverter.ToUInt32(s.Data, 20));
						}
					}
					return 0;
				}
			case "diff":
				{
					var c = Plugin.Load(args[1]);
					var u = lo.Load(Analyze.UssepName);
					foreach (var r in c.ByFormId.Values)
					{
						var ur = u.ByFormId[r.FormId];
						Func<Record, List<string>> keys = x => x.Subs().Select(s => s.Type + ":" + BitConverter.ToString(s.Data)).ToList();
						var a = keys(ur);
						var b = keys(r);
						var removed = new List<string>(a);
						foreach (var k in b)
							removed.Remove(k);
						var added = new List<string>(b);
						foreach (var k in a)
							added.Remove(k);
						string hf = ur.Flags == r.Flags ? "" : string.Format(" flags {0:X}->{1:X}", ur.Flags, r.Flags);
						Console.WriteLine("{0} {1:X8} {2} -{3} +{4}{5}", r.Type, r.FormId, r.EditorId(), removed.Count, added.Count, hf);
						foreach (var k in removed.Concat(added.Select(x => "+" + x)))
							if (!k.StartsWith("CTDA") && !k.StartsWith("EFID") && !k.StartsWith("EFIT") && !k.StartsWith("+CTDA"))
								Console.WriteLine("    " + (k.Length > 110 ? k.Substring(0, 110) : k));
					}
					return 0;
				}
			case "find":
				{
					var pl = lo.Load(args[1]);
					var rx = new System.Text.RegularExpressions.Regex(args[2], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
					foreach (var r in pl.ByFormId.Values)
					{
						string ed = r.EditorId();
						if (!rx.IsMatch(ed))
							continue;
						string grid = "";
						foreach (var s in r.Subs())
							if (s.Type == "XCLC")
								grid = string.Format(" grid {0} {1}", BitConverter.ToInt32(s.Data, 0), BitConverter.ToInt32(s.Data, 4));
						Console.WriteLine("{0} {1:X8} {2}{3}", r.Type, r.FormId, ed, grid);
					}
					return 0;
				}
			case "subs":
				{
					var pl = lo.Load(args[1]);
					foreach (var a in args.Skip(2))
					{
						var r = pl.ByFormId[Convert.ToUInt32(a, 16)];
						Console.WriteLine("{0} {1:X8} {2} flags={3:X}: {4}", r.Type, r.FormId, r.EditorId(), r.Flags, string.Join(" ", r.Subs().Select(s => s.Type + (s.Data.Length <= 8 ? "=" + BitConverter.ToString(s.Data).Replace("-", "") : ""))));
					}
					return 0;
				}
			case "orphans":
				{
					var u = lo.Load(Analyze.UssepName);
					foreach (var r in u.ByFormId.Values)
					{
						if (r.Type != "REFR" && r.Type != "ACHR" && r.Type != "PGRE" && r.Type != "PHZD" && r.Type != "PMIS" && r.Type != "PARW" && r.Type != "PBEA" && r.Type != "PFLA" && r.Type != "PCON" && r.Type != "PBAR")
							continue;
						var name = r.Subs().FirstOrDefault(s => s.Type == "NAME");
						if (name == null)
							continue;
						uint b = BitConverter.ToUInt32(name.Data, 0);
						if ((b >> 24) >= u.Masters.Count || !Analyze.IsCc(u.FileOf(b)))
							continue;
						Record br;
						string state = u.ByFormId.TryGetValue(b, out br) ? "USSEP-defines " + br.Type + " " + br.EditorId() : "MISSING";
						Console.WriteLine("{0} {1:X8} flags={2:X} {3} base={4:X8} {5}", r.Type, r.FormId, r.Flags, r.EditorId(), b, state);
					}
					return 0;
				}
			case "scanlo":
				{
					var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (var l in System.IO.File.ReadAllLines(System.IO.Path.Combine(lo.Profile, "plugins.txt")))
						if (l.StartsWith("*"))
							active.Add(l.Substring(1).Trim());
					foreach (var p in lo.Plugins())
					{
						if (p.StartsWith("UNRESOLVED:"))
						{
							Console.WriteLine(p);
							continue;
						}
						string name = System.IO.Path.GetFileName(p);
						var pl = Plugin.Load(p);
						var ccm = pl.Masters.Where(Analyze.IsCc).ToList();
						if (ccm.Count == 0 && !Analyze.IsCc(name))
							continue;
						int ccRecs = pl.ByFormId.Keys.Count(f => (f >> 24) < pl.Masters.Count && Analyze.IsCc(pl.Masters[(int)(f >> 24)]));
						Console.WriteLine("{0}{1} [{2}] ccMasters={3} ccRecords={4} records={5}", active.Contains(name) ? "*" : " ", name, p, string.Join(",", ccm), ccRecs, pl.ByFormId.Count);
					}
					return 0;
				}
			default:
				return Usage();
			}
		}

		static int Usage()
		{
			Console.WriteLine("usage: ccfree <analyze|build|scanlo|dump|verify|diff|conds> <MO2 instance> <profile> [out.txt|outDir|fid...|esp [fid...]]");
			return 1;
		}
	}
}
