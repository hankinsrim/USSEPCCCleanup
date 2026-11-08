using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CCFree
{
	public class OutRecord
	{
		public Record Src;
		public uint Flags;
		public List<Sub> Subs;
	}

	public class OutGroup
	{
		public Group Src;
		public List<object> Children = new List<object>();
	}

	public static class Writer
	{
		public static byte[] SubBytes(IEnumerable<Sub> subs)
		{
			var ms = new MemoryStream();
			var w = new BinaryWriter(ms);
			foreach (var s in subs)
			{
				if (s.Data.Length > 0xFFFF)
				{
					w.Write(Encoding.ASCII.GetBytes("XXXX"));
					w.Write((ushort)4);
					w.Write((uint)s.Data.Length);
					w.Write(Encoding.ASCII.GetBytes(s.Type));
					w.Write((ushort)0);
				}
				else
				{
					w.Write(Encoding.ASCII.GetBytes(s.Type));
					w.Write((ushort)s.Data.Length);
				}
				w.Write(s.Data);
			}
			return ms.ToArray();
		}

		public static Sub Str(string type, string value) => new Sub(type, Encoding.ASCII.GetBytes(value + "\0"));

		public static byte[] Header(uint flags, IList<string> masters, int numRecords, string author, string description)
		{
			var subs = new List<Sub>();
			var hedr = new byte[12];
			BitConverter.GetBytes(1.7f).CopyTo(hedr, 0);
			BitConverter.GetBytes(numRecords).CopyTo(hedr, 4);
			BitConverter.GetBytes(0x800u).CopyTo(hedr, 8);
			subs.Add(new Sub("HEDR", hedr));
			subs.Add(Str("CNAM", author));
			if (!string.IsNullOrEmpty(description))
				subs.Add(Str("SNAM", description));
			foreach (var m in masters)
			{
				subs.Add(Str("MAST", m));
				subs.Add(new Sub("DATA", new byte[8]));
			}
			byte[] data = SubBytes(subs);
			var ms = new MemoryStream();
			var w = new BinaryWriter(ms);
			w.Write(Encoding.ASCII.GetBytes("TES4"));
			w.Write((uint)data.Length);
			w.Write(flags);
			w.Write(0u);
			w.Write(0u);
			w.Write((ushort)44);
			w.Write((ushort)0);
			w.Write(data);
			return ms.ToArray();
		}

		public static void WriteRecord(BinaryWriter w, OutRecord r)
		{
			byte[] data;
			uint flags = r.Flags;
			if (r.Subs == null)
				data = r.Src.Raw;
			else
			{
				if (r.Subs.Contains(null))
					throw new Exception(string.Format("null subrecord in {0:X8}", r.Src.FormId));
				data = SubBytes(r.Subs);
				flags &= ~0x40000u;
			}
			w.Write(Encoding.ASCII.GetBytes(r.Src.Type));
			w.Write((uint)data.Length);
			w.Write(flags);
			w.Write(r.Src.FormId);
			w.Write(r.Src.Tail);
			w.Write(data);
		}

		public static int Count(IEnumerable<object> nodes) => nodes.Sum(n => n is OutGroup g ? 1 + Count(g.Children) : 1);

		public static void WriteGroup(BinaryWriter w, OutGroup g)
		{
			long start = w.BaseStream.Position;
			w.Write(g.Src.Header);
			foreach (var c in g.Children)
			{
				if (c is OutGroup cg)
					WriteGroup(w, cg);
				else
					WriteRecord(w, (OutRecord)c);
			}
			long end = w.BaseStream.Position;
			w.BaseStream.Position = start + 4;
			w.Write((uint)(end - start));
			w.BaseStream.Position = end;
		}
	}

	// Mirrors the source plugin's group hierarchy for a chosen set of records.
	public class Tree
	{
		public List<object> Top = new List<object>();
		readonly Dictionary<Node, object> map = new Dictionary<Node, object>();
		readonly Plugin src;

		public Tree(Plugin src) { this.src = src; }

		List<object> ChildList(Group parent) => parent == null ? Top : ((OutGroup)Ensure(parent)).Children;

		int IndexOf(Node n) => n.Parent == null ? src.Top.IndexOf(n) : n.Parent.Children.IndexOf(n);

		object Ensure(Node n)
		{
			object o;
			if (map.TryGetValue(n, out o))
				return o;
			if (n is Group g && (g.GroupType == 1 || g.GroupType == 6 || g.GroupType == 7))
			{
				Record owner = src.ByFormId[g.Label];
				Ensure(owner);
			}
			o = n is Group gg ? (object)new OutGroup { Src = gg } : new OutRecord { Src = (Record)n, Flags = ((Record)n).Flags };
			map[n] = o;
			var list = ChildList(n.Parent);
			int idx = IndexOf(n);
			int pos = list.FindIndex(x => IndexOf(SrcOf(x)) > idx);
			if (pos < 0)
				list.Add(o);
			else
				list.Insert(pos, o);
			return o;
		}

		static Node SrcOf(object o) => o is OutGroup g ? (Node)g.Src : ((OutRecord)o).Src;

		public OutRecord Add(Record r) => (OutRecord)Ensure(r);
	}
}
