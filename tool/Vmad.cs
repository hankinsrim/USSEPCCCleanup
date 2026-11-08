using System;
using System.Collections.Generic;
using System.Text;

namespace CCFree
{
	public static class Vmad
	{
		// Yields script.property -> FormID for object properties in the script list (fragments are not parsed).
		public static List<KeyValuePair<string, uint>> Objects(byte[] d) => Offsets(d).ConvertAll(t => new KeyValuePair<string, uint>(t.Item1, t.Item2));

		// Same as Objects but also returns the byte offset of each FormID.
		public static List<Tuple<string, uint, int>> Offsets(byte[] d)
		{
			var list = new List<Tuple<string, uint, int>>();
			int p = 0;
			int version = BitConverter.ToInt16(d, p);
			int objFormat = BitConverter.ToInt16(d, p + 2);
			int count = BitConverter.ToUInt16(d, p + 4);
			p += 6;
			try
			{
				for (int i = 0; i < count; i++)
				{
					string script = Str(d, ref p);
					if (version >= 4)
						p++;
					int props = BitConverter.ToUInt16(d, p);
					p += 2;
					for (int j = 0; j < props; j++)
					{
						string name = script + "." + Str(d, ref p);
						int type = d[p++];
						if (version >= 4)
							p++;
						switch (type)
						{
						case 1:
							list.Add(Tuple.Create(name, Obj(d, p, objFormat), Off(p, objFormat)));
							p += 8;
							break;
						case 2:
							Str(d, ref p);
							break;
						case 3:
						case 4:
							p += 4;
							break;
						case 5:
							p += 1;
							break;
						case 11:
							{
								int n = BitConverter.ToInt32(d, p);
								p += 4;
								for (int k = 0; k < n; k++, p += 8)
									list.Add(Tuple.Create(name + "[" + k + "]", Obj(d, p, objFormat), Off(p, objFormat)));
								break;
							}
						case 12:
							{
								int n = BitConverter.ToInt32(d, p);
								p += 4;
								for (int k = 0; k < n; k++)
									Str(d, ref p);
								break;
							}
						case 13:
						case 14:
							p += 4 + 4 * BitConverter.ToInt32(d, p);
							break;
						case 15:
							p += 4 + BitConverter.ToInt32(d, p);
							break;
						default:
							throw new Exception("bad property type " + type);
						}
					}
				}
			}
			catch (Exception e)
			{
				list.Add(Tuple.Create("PARSE-ERROR " + e.Message, 0u, -1));
			}
			return list;
		}

		static int Off(int p, int format) => format == 1 ? p : p + 4;

		static uint Obj(byte[] d, int p, int format) => format == 1 ? BitConverter.ToUInt32(d, p) : BitConverter.ToUInt32(d, p + 4);

		static string Str(byte[] d, ref int p)
		{
			int n = BitConverter.ToUInt16(d, p);
			string s = Encoding.ASCII.GetString(d, p + 2, n);
			p += 2 + n;
			return s;
		}
	}
}
