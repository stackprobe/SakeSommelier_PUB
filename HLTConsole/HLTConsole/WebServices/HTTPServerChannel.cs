using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using HLTStudio.Commons;

namespace HLTStudio.WebServices
{
	public class HTTPServerChannel
	{
		/// <summary>
		/// 要求タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int RequestTimeoutMillis = -1;

		/// <summary>
		/// 応答タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int ResponseTimeoutMillis = -1;

		// memo: チャンク毎のタイムアウトは IdleTimeoutMillis で代替する。

		/// <summary>
		/// リクエストの最初の行のみの無通信タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int FirstLineTimeoutMillis = 2000;

		/// <summary>
		/// リクエストの最初の行以外の(レスポンスも含む)無通信タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int IdleTimeoutMillis = 180000; // 3 min

		/// <summary>
		/// リクエストのボディの最大サイズ_バイト数
		/// -1 == INFINITE
		/// </summary>
		public static long BodySizeMax = 512000000; // 512 MB

		/// <summary>
		/// リクエストのボディをストレージ上でバッファリングする。
		/// </summary>
		public static bool BodyOnStorage = false;

		// <---- init if needed

		public SockChannel Channel;

		public IEnumerable<int> RecvRequest()
		{
			this.Channel.SessionTimeoutTime = TimeoutMillisToDateTime(RequestTimeoutMillis);
			this.Channel.CurrIdleTimeoutMillis = FirstLineTimeoutMillis;

			this.Channel.FirstLineRecving = true;

			foreach (var relay in this.RecvLine(ret => this.FirstLine = ret))
				yield return relay;

			this.Channel.FirstLineRecving = false;

			{
				string[] tokens = SCommon.Tokenize(this.FirstLine, " ");

				this.Method = tokens[0];
				this.PathQuery = DecodeURL(tokens[1]);
				this.HTTPVersion = tokens[2];
			}

			this.Channel.CurrIdleTimeoutMillis = IdleTimeoutMillis;

			foreach (var relay in this.RecvHeader())
				yield return relay;

			this.CheckHeader();

			if (this.Expect100Continue)
			{
				foreach (var relay in this.SendLine("HTTP/1.1 100 CONTINUE")
					.Concat(this.Channel.Send(CRLF)))
					yield return relay;
			}
			foreach (var relay in this.RecvBody())
				yield return relay;
		}

		private static DateTime? TimeoutMillisToDateTime(int millis)
		{
			if (millis == -1)
				return null;

			return DateTime.Now + TimeSpan.FromMilliseconds((double)millis);
		}

		private static string DecodeURL(string path)
		{
			byte[] src = Encoding.ASCII.GetBytes(path);

			using (MemoryStream writer = new MemoryStream())
			{
				for (int index = 0; index < src.Length; index++)
				{
					if (src[index] == 0x25 && index + 2 <= src.Length) // ? '%'
					{
						writer.WriteByte((byte)Convert.ToInt32(Encoding.ASCII.GetString(P_GetBytesRange(src, index + 1, 2)), 16));
						index += 2;
					}
					else if (src[index] == 0x2b) // ? '+'
					{
						writer.WriteByte(0x20); // ' '
					}
					else
					{
						writer.WriteByte(src[index]);
					}
				}

				byte[] bytes = writer.ToArray();

				if (!SockCommon.P_UTF8Check.Check(bytes))
					throw new Exception("URL is not Japanese UTF-8");

				return Encoding.UTF8.GetString(bytes);
			}
		}

		private static byte[] P_GetBytesRange(byte[] src, int offset, int size)
		{
			byte[] dest = new byte[size];
			Array.Copy(src, offset, dest, 0, size);
			return dest;
		}

		public string FirstLine;
		public string Method;
		public string PathQuery;
		public string HTTPVersion;
		public List<string[]> HeaderPairs = new List<string[]>();
		public HTTPBodyOutputStream.IBOS Body;

		private const byte CR = 0x0d;
		private const byte LF = 0x0a;

		private static readonly byte[] CRLF = new byte[] { CR, LF };

		private IEnumerable<int> RecvLine(Action<string> a_return)
		{
			const int LINE_LEN_MAX = 128 * 1024;

			using (MemoryStream writer = new MemoryStream())
			{
				int wroteSize = 0;

				for (; ; )
				{
					byte[] chrs = null;

					foreach (var relay in this.Channel.Recv(1, ret => chrs = ret))
						yield return relay;

					byte chr = chrs[0];

					if (chr == CR)
						continue;

					if (chr == LF)
						break;

					if (LINE_LEN_MAX <= wroteSize)
						throw new OverflowException("Received line is too long");

					if (chr < 0x20 || 0x7e < chr) // ? not ASCII -> SPACE
						chr = 0x20;

					writer.WriteByte(chr);
					wroteSize++;
				}
				a_return(Encoding.ASCII.GetString(writer.ToArray()));
			}
		}

		private IEnumerable<int> RecvHeader()
		{
			const int WEIGHT = 256;
			const int HEADER_LEN_MAX = 128 * 1024 + 256 * WEIGHT;

			int roughHeaderLength = 0;

			for (; ; )
			{
				string line = null;

				foreach (var relay in this.RecvLine(ret => line = ret))
					yield return relay;

				if (line == null)
					throw null; // never

				if (line == "")
					break;

				roughHeaderLength += line.Length + WEIGHT;

				if (HEADER_LEN_MAX < roughHeaderLength)
					throw new OverflowException("Received header is too long");

				if (line[0] <= ' ') // HACK: 行折り畳み(line folding)対応 -- 行折り畳みは廃止されたっぽいけど念のため対応しておく。
				{
					this.HeaderPairs[this.HeaderPairs.Count - 1][1] += " " + line.Trim();
				}
				else
				{
					int colon = line.IndexOf(':');

					if (colon == -1)
						throw new Exception("Bad header line (no colon)");

					this.HeaderPairs.Add(new string[]
					{
						line.Substring(0, colon).Trim(),
						line.Substring(colon + 1).Trim(),
					});
				}
			}
		}

		public long ContentLength = 0;
		public bool Chunked = false;
		public string ContentType = null;
		public bool Expect100Continue = false;
		public bool KeepAlive = false;

		private void CheckHeader()
		{
			foreach (string[] pair in this.HeaderPairs)
			{
				string key = pair[0];
				string value = pair[1];

				if (1000 < key.Length || 1000 < value.Length) // rough limit
				{
					SockCommon.WriteLog(SockCommon.ErrorLevel_e.INFO, "Ignore gen-header key and value (too long)");
					continue;
				}

				if (key.EqualsIgnoreCase("Content-Length"))
				{
					if (value.Length < 1 || 19 < value.Length)
						throw new Exception("Bad Content-Length value");

					this.ContentLength = long.Parse(value);
				}
				else if (key.EqualsIgnoreCase("Transfer-Encoding"))
				{
					this.Chunked = value.ContainsIgnoreCase("chunked");
				}
				else if (key.EqualsIgnoreCase("Content-Type"))
				{
					this.ContentType = value;
				}
				else if (key.EqualsIgnoreCase("Expect"))
				{
					this.Expect100Continue = value.ContainsIgnoreCase("100-continue");
				}
				else if (key.EqualsIgnoreCase("Connection"))
				{
					this.KeepAlive = value.ContainsIgnoreCase("keep-alive");
				}
			}
		}

		private IEnumerable<int> RecvBody()
		{
			const int READ_SIZE_MAX = 1024 * 1024;

			HTTPBodyOutputStream.IBOS buff = this.Channel.BodyOutputStream;

			if (this.Chunked)
			{
				for (; ; )
				{
					string line = null;

					foreach (var relay in this.RecvLine(ret => line = ret))
						yield return relay;

					if (line == null)
						throw null; // never

					// chunk-extension の削除
					{
						int i = line.IndexOf(';');

						if (i != -1)
							line = line.Substring(0, i);
					}

					line = line.Trim();

					if (line.Length < 1 || 8 < line.Length)
						throw new Exception("Bad chunk-size line");

					int size = Convert.ToInt32(line, 16);

					if (size == 0)
						break;

					if (size < 0)
						throw new Exception("不正なチャンクサイズです。" + size);

					if (BodySizeMax != -1 && BodySizeMax - buff.GetWroteSize() < (long)size)
						throw new Exception($"ボディサイズが大きすぎます。{buff.GetWroteSize()} + {size}");

					long chunkEnd = buff.GetWroteSize() + (long)size;

					while (buff.GetWroteSize() < chunkEnd)
					{
						byte[] data = null;

						foreach (var relay in this.Channel.Recv((int)Math.Min((long)READ_SIZE_MAX, chunkEnd - buff.GetWroteSize()), ret => data = ret))
							yield return relay;

						if (data == null)
							throw null; // never

						buff.Write(data);
					}
					foreach (var relay in this.Channel.Recv(2, ret => { })) // CR-LF
						yield return relay;
				}

				for (; ; ) // RFC 7230 4.1.2 Chunked Trailer Part
				{
					string line = null;

					foreach (var relay in this.RecvLine(ret => line = ret))
						yield return relay;

					if (line == null)
						throw null; // never

					if (line == "")
						break;
				}
			}
			else
			{
				if (this.ContentLength < 0)
					throw new Exception("不正なボディサイズです。" + this.ContentLength);

				if (BodySizeMax != -1 && BodySizeMax < this.ContentLength)
					throw new Exception("ボディサイズが大きすぎます。" + this.ContentLength);

				while (buff.GetWroteSize() < this.ContentLength)
				{
					byte[] data = null;

					foreach (var relay in this.Channel.Recv((int)Math.Min((long)READ_SIZE_MAX, this.ContentLength - buff.GetWroteSize()), ret => data = ret))
						yield return relay;

					if (data == null)
						throw null; // never

					buff.Write(data);
				}
			}
			this.Body = buff;
		}

		// HTTPConnected 内で(必要に応じて)設定しなければならないフィールド -->

		public int ResStatus = 200;
		public List<string[]> ResHeaderPairs = new List<string[]>();
		public IEnumerable<byte[]> ResBody = null; // ゼロバイトの要素を含んでも良い。null == 応答ボディ無し(ゼロバイトの応答ボディではないことに注意)
		public long ResBodyLength = -1L; // 応答ボディの長さをセットすること。-1L == チャンクで応答する。

		// <-- HTTPConnected 内で(必要に応じて)設定しなければならないフィールド

		public IEnumerable<int> SendResponse()
		{
			this.Body = null;
			this.Channel.SessionTimeoutTime = TimeoutMillisToDateTime(ResponseTimeoutMillis);

			foreach (var relay in this.SendLine($"HTTP/1.1 {this.ResStatus} COFFEE READY"))
				yield return relay;

			foreach (string[] pair in this.ResHeaderPairs)
				foreach (var relay in this.SendLine($"{pair[0]}: {pair[1]}"))
					yield return relay;

			if (this.ResBody == null)
			{
				foreach (var relay in this.EndHeader())
					yield return relay;
			}
			else if (this.ResBodyLength != -1L)
			{
				foreach (var relay in this.SendLine($"Content-Length: {this.ResBodyLength}")
					.Concat(this.EndHeader()))
					yield return relay;

				long sentLength = 0L;

				foreach (byte[] resBodyPart in this.ResBody)
				{
					foreach (var relay in this.Channel.Send(resBodyPart))
						yield return relay;

					sentLength += resBodyPart.Length;
				}
				if (sentLength != this.ResBodyLength)
					throw new Exception("Bad ResBodyLength");
			}
			else
			{
				IEnumerator<byte[]> resBodyIterator = this.ResBody.GetEnumerator();

				if (SockCommon.NB("chu1", () => resBodyIterator.MoveNext()))
				{
					byte[] first = resBodyIterator.Current;

					if (SockCommon.NB("chu2", () => resBodyIterator.MoveNext()))
					{
						foreach (var relay in this.SendLine("Transfer-Encoding: chunked")
							.Concat(this.EndHeader())
							.Concat(this.SendChunk(first)))
							yield return relay;

						first = null;

						do
						{
							foreach (var relay in this.SendChunk(resBodyIterator.Current))
								yield return relay;
						}
						while (SockCommon.NB("chux", () => resBodyIterator.MoveNext()));

						foreach (var relay in this.SendLine("0")
							.Concat(this.Channel.Send(CRLF)))
							yield return relay;
					}
					else
					{
						foreach (var relay in this.SendLine($"Content-Length: {first.Length}")
							.Concat(this.EndHeader())
							.Concat(this.Channel.Send(first)))
							yield return relay;
					}
				}
				else
				{
					foreach (var relay in this.SendLine("Content-Length: 0")
						.Concat(this.EndHeader()))
						yield return relay;
				}
			}
		}

		private IEnumerable<int> EndHeader()
		{
			foreach (var relay in this.SendLine($"Connection: {(this.KeepAlive ? "keep-alive" : "close")}")
				.Concat(this.Channel.Send(CRLF)))
				yield return relay;
		}

		private IEnumerable<int> SendChunk(byte[] chunk)
		{
			if (1 <= chunk.Length)
			{
				foreach (var relay in this.SendLine(chunk.Length.ToString("x"))
					.Concat(this.Channel.Send(chunk))
					.Concat(this.Channel.Send(CRLF)))
					yield return relay;
			}
		}

		private IEnumerable<int> SendLine(string line)
		{
			foreach (var relay in this.Channel.Send(Encoding.ASCII.GetBytes(line))
				.Concat(this.Channel.Send(CRLF)))
				yield return relay;
		}
	}
}
