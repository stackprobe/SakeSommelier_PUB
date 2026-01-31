using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using HLTStudio.Commons;

namespace HLTStudio.Tools
{
	public static class JsonTools
	{
		public class Node
		{
			public class Pair
			{
				public string Name;
				public Node Value;
			}

			public List<Node> Array;
			public List<Pair> Map;
			public string StringValue;
			public string WordValue;

			public Node this[int index]
			{
				get
				{
					return this.Array[index];
				}
			}

			public Node this[string name]
			{
				get
				{
					return this.Map.First(v => v.Name == name).Value;
				}
			}
		}

		public static Node LoadFromFile(string file)
		{
			return LoadFromFile(file, GetFileEncoding(file));
		}

		public static Node LoadFromFile(string file, Encoding encoding)
		{
			return Load(File.ReadAllText(file, encoding));
		}

		public static Node Load(string text)
		{
			return new Reader(text).GetNode();
		}

		private class Reader
		{
			private string Text;
			private int Index = 0;

			public Reader(string text)
			{
				this.Text = text;
			}

			private char Next()
			{
				return this.Text[this.Index++];
			}

			private char NextNS()
			{
				char chr;

				do
				{
					chr = this.Next();
				}
				while (chr <= ' ');

				return chr;
			}

			public Node GetNode()
			{
				char chr = this.NextNS();
				Node node = new Node();

				if (chr == '[') // ? 配列
				{
					node.Array = new List<Node>();

					if ((chr = this.NextNS()) != ']')
					{
						for (; ; )
						{
							this.Index--;
							node.Array.Add(this.GetNode());
							chr = this.NextNS();

							if (chr == ']')
								break;

							if (chr != ',')
								throw new Exception("JSON format error: Array ','");

							chr = this.NextNS();

							if (chr == ']')
							{
								ProcMain.WriteLog("JSON format warning: ',' before ']'");
								break;
							}
						}
					}
				}
				else if (chr == '{') // ? 連想配列
				{
					node.Map = new List<Node.Pair>();

					if ((chr = this.NextNS()) != '}')
					{
						for (; ; )
						{
							this.Index--;
							Node nameNode = this.GetNode();
							string name = nameNode.StringValue ?? nameNode.WordValue;

							if (name == null)
								throw new Exception("JSON format error: Map name");

							if (this.NextNS() != ':')
								throw new Exception("JSON format error: Map ':'");

							node.Map.Add(new Node.Pair()
							{
								Name = name,
								Value = this.GetNode(),
							});

							chr = this.NextNS();

							if (chr == '}')
								break;

							if (chr != ',')
								throw new Exception("JSON format error: Map ','");

							chr = this.NextNS();

							if (chr == '}')
							{
								ProcMain.WriteLog("JSON format warning: ',' before '}'");
								break;
							}
						}
					}
				}
				else if (chr == '"' || chr == '\'') // ? 文字列
				{
					StringBuilder buff = new StringBuilder();
					char chrEnclStr = chr;

					if (chrEnclStr == '\'')
						ProcMain.WriteLog("JSON format warning: String enclosed in single quotes");

					for (; ; )
					{
						chr = this.Next();

						if (chr == chrEnclStr)
							break;

						if (chr == '\\')
						{
							chr = this.Next();

							if (chr == 'b')
							{
								chr = '\b';
							}
							else if (chr == 'f')
							{
								chr = '\f';
							}
							else if (chr == 'n')
							{
								chr = '\n';
							}
							else if (chr == 'r')
							{
								chr = '\r';
							}
							else if (chr == 't')
							{
								chr = '\t';
							}
							else if (chr == 'u')
							{
								char c1 = this.Next();
								char c2 = this.Next();
								char c3 = this.Next();
								char c4 = this.Next();

								chr = (char)Convert.ToInt32(new string(new char[] { c1, c2, c3, c4 }), 16);
							}
						}
						buff.Append(chr);
					}
					node.StringValue = buff.ToString();
				}
				else // ? 単語
				{
					StringBuilder buff = new StringBuilder();

					this.Index--;

					while (this.Index < this.Text.Length)
					{
						chr = this.Next();

						if (
							chr == '}' ||
							chr == ']' ||
							chr == ',' ||
							chr == ':'
							)
						{
							this.Index--;
							break;
						}
						buff.Append(chr);
					}
					node.WordValue = buff.ToString().Trim();
				}
				return node;
			}
		}

		private static Encoding GetFileEncoding(string file)
		{
			using (FileStream reader = new FileStream(file, FileMode.Open, FileAccess.Read))
			{
				byte[] buff = new byte[4];
				int readSize = reader.Read(buff, 0, 4);

				// ? UTF-32 BE
				if (
					4 <= readSize &&
					buff[0] == 0x00 &&
					buff[1] == 0x00 &&
					buff[2] == 0xfe &&
					buff[3] == 0xff
					)
					return Encoding.UTF32;

				// ? UTF-32 LE
				if (
					4 <= readSize &&
					buff[0] == 0xff &&
					buff[1] == 0xfe &&
					buff[2] == 0x00 &&
					buff[3] == 0x00
					)
					return Encoding.UTF32;

				// ? UTF-16 BE
				if (
					2 <= readSize &&
					buff[0] == 0xfe &&
					buff[1] == 0xff
					)
					return Encoding.Unicode;

				// ? UTF-16 LE
				if (
					2 <= readSize &&
					buff[0] == 0xff &&
					buff[1] == 0xfe
					)
					return Encoding.Unicode;

				return Encoding.UTF8;
			}
		}

		public static void WriteToFile(Node node, string file)
		{
			WriteToFile(node, file, Encoding.UTF8);
		}

		public static void WriteToFile(Node node, string file, Encoding encoding)
		{
			File.WriteAllText(file, GetString(node), encoding);
		}

		public static string GetString(Node node)
		{
			StringBuilder buff = new StringBuilder();
			new Writer(buff).WriteRoot(node);
			return buff.ToString();
		}

		private class Writer
		{
			private StringBuilder Buff;
			private int Depth = 0;

			public Writer(StringBuilder buff)
			{
				this.Buff = buff;
			}

			public void WriteRoot(Node node)
			{
				this.Write(node);
				this.WriteNewLine();
			}

			public void Write(Node node)
			{
				if (node.Array != null) // ? 配列
				{
					this.Write('[');
					this.WriteNewLine();
					this.Depth++;

					for (int index = 0; index < node.Array.Count; index++)
					{
						this.WriteIndent();
						this.Write(node.Array[index]);

						if (index < node.Array.Count - 1)
							this.Write(',');

						this.WriteNewLine();
					}
					this.Depth--;
					this.WriteIndent();
					this.Write(']');
				}
				else if (node.Map != null) // ? 連想配列
				{
					this.Write('{');
					this.WriteNewLine();
					this.Depth++;

					for (int index = 0; index < node.Map.Count; index++)
					{
						this.WriteIndent();
						this.Write(new Node() { StringValue = node.Map[index].Name });
						this.Write(':');
						this.WriteSpace();
						this.Write(node.Map[index].Value);

						if (index < node.Map.Count - 1)
							this.Write(',');

						this.WriteNewLine();
					}
					this.Depth--;
					this.WriteIndent();
					this.Write('}');
				}
				else if (node.StringValue != null) // ? 文字列
				{
					this.Write('"');

					foreach (char chr in node.StringValue)
					{
						if (chr == '"')
						{
							this.Write("\\\"");
						}
						else if (chr == '\\')
						{
							this.Write("\\\\");
						}
						else if (chr == '/')
						{
							//this.Write("\\/");
							this.Write("\\u002F");
						}
						else if (chr == '\b')
						{
							this.Write("\\b");
						}
						else if (chr == '\f')
						{
							this.Write("\\f");
						}
						else if (chr == '\n')
						{
							this.Write("\\n");
						}
						else if (chr == '\r')
						{
							this.Write("\\r");
						}
						else if (chr == '\t')
						{
							this.Write("\\t");
						}
						else
						{
							this.Write(chr);
						}
					}
					this.Write('"');
				}
				else if (node.WordValue != null) // ? 単語
				{
					this.Write(node.WordValue);
				}
				else
				{
					throw new Exception("JSON node error");
				}
			}

			private void WriteIndent()
			{
				this.Write(new string('\t', this.Depth));
			}

			private void WriteNewLine()
			{
				this.Write("\r\n");
			}

			private void WriteSpace()
			{
				this.Write(' ');
			}

			private void Write(string str)
			{
				this.Buff.Append(str);
			}

			private void Write(char chr)
			{
				this.Buff.Append(chr);
			}
		}
	}
}
