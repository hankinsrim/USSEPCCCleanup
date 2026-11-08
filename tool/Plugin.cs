using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CCFree
{
	public class Node
	{
		public Group Parent;
	}

	public class Group : Node
	{
		public byte[] Header = new byte[24];
		public List<Node> Children = new List<Node>();
		public uint Label => BitConverter.ToUInt32(Header, 8);
		public int GroupType => BitConverter.ToInt32(Header, 12);
	}

	public class Sub
	{
		public string Type;
		public byte[] Data;
		public Sub(string type, byte[] data) { Type = type; Data = data; }
	}

	public class Record : Node
	{
		public string Type;
		public uint Flags;
		public uint FormId;
		public byte[] Tail = new byte[8];
		public byte[] Raw;
		public Plugin Owner;

		public bool Compressed => (Flags & 0x40000) != 0;

		public byte[] Data()
		{
			if (!Compressed)
				return Raw;
			int size = BitConverter.ToInt32(Raw, 0);
			var outBuf = new byte[size];
			using (var ms = new MemoryStream(Raw, 6, Raw.Length - 6))
			using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
			{
				int read = 0;
				while (read < size)
				{
					int n = ds.Read(outBuf, read, size - read);
					if (n <= 0)
						break;
					read += n;
				}
			}
			return outBuf;
		}

		public List<Sub> Subs()
		{
			var list = new List<Sub>();
			byte[] d = Data();
			int p = 0;
			uint bigSize = 0;
			while (p + 6 <= d.Length)
			{
				string t = Encoding.ASCII.GetString(d, p, 4);
				int sz = BitConverter.ToUInt16(d, p + 4);
				p += 6;
				if (t == "XXXX")
				{
					bigSize = BitConverter.ToUInt32(d, p);
					p += sz;
					continue;
				}
				if (bigSize != 0)
				{
					sz = (int)bigSize;
					bigSize = 0;
				}
				var b = new byte[sz];
				Array.Copy(d, p, b, 0, sz);
				list.Add(new Sub(t, b));
				p += sz;
			}
			return list;
		}

		public string EditorId()
		{
			foreach (var s in Subs())
				if (s.Type == "EDID")
					return Encoding.ASCII.GetString(s.Data).TrimEnd('\0');
			return "";
		}
	}

	public class Plugin
	{
		public string Name;
		public string Path;
		public Record Header;
		public List<string> Masters = new List<string>();
		public List<Node> Top = new List<Node>();
		public Dictionary<uint, Record> ByFormId = new Dictionary<uint, Record>();

		public static Plugin Load(string path)
		{
			var pl = new Plugin { Path = path, Name = System.IO.Path.GetFileName(path) };
			byte[] b = File.ReadAllBytes(path);
			int pos = 0;
			var stack = new List<KeyValuePair<int, Group>>();
			while (pos + 24 <= b.Length)
			{
				while (stack.Count > 0 && pos >= stack[stack.Count - 1].Key)
					stack.RemoveAt(stack.Count - 1);
				Group parent = stack.Count > 0 ? stack[stack.Count - 1].Value : null;
				string type = Encoding.ASCII.GetString(b, pos, 4);
				uint size = BitConverter.ToUInt32(b, pos + 4);
				Node node;
				if (type == "GRUP")
				{
					var g = new Group();
					Array.Copy(b, pos, g.Header, 0, 24);
					stack.Add(new KeyValuePair<int, Group>(pos + (int)size, g));
					pos += 24;
					node = g;
				}
				else
				{
					var r = new Record { Type = type, Flags = BitConverter.ToUInt32(b, pos + 8), FormId = BitConverter.ToUInt32(b, pos + 12), Owner = pl };
					Array.Copy(b, pos + 16, r.Tail, 0, 8);
					r.Raw = new byte[size];
					Array.Copy(b, pos + 24, r.Raw, 0, size);
					pos += 24 + (int)size;
					node = r;
					if (pl.Header == null)
					{
						pl.Header = r;
						foreach (var s in r.Subs())
							if (s.Type == "MAST")
								pl.Masters.Add(Encoding.ASCII.GetString(s.Data).TrimEnd('\0'));
						continue;
					}
					pl.ByFormId[r.FormId] = r;
				}
				node.Parent = parent;
				if (parent != null)
					parent.Children.Add(node);
				else
					pl.Top.Add(node);
			}
			return pl;
		}

		public string FileOf(uint fid)
		{
			int idx = (int)(fid >> 24);
			return idx < Masters.Count ? Masters[idx] : Name;
		}

		public bool IsLight => (Header.Flags & 0x200) != 0 || Name.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
	}
}
