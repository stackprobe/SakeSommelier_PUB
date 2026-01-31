using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using HLTStudio.Commons;
using HLTStudio.WebServices;
using System.Threading;
using HLTStudio.Tools;
using HLTStudio.Modules;

namespace HLTStudio
{
	class Program
	{
		static void Main(string[] args)
		{
			ProcMain.CUIMain(new Program().Main2);
		}

		private void Main2(ArgsReader ar)
		{
			if (ProcMain.DEBUG)
			{
				Main3();
			}
			else
			{
				Main4(ar);
			}
			SCommon.OpenOutputDirIfCreated();
		}

		private void Main3()
		{
#if DEBUG
			string apiKey = File.ReadAllText(@"C:\home\admin\GeminiAPIKey.txt", Encoding.ASCII).Trim();

			// -- choose one --

			Main4(new ArgsReader(new string[] { "/K", apiKey }));
			//Main4(new ArgsReader(new string[] { }));
			//Main4(new ArgsReader(new string[] { }));

			// --
#endif
			SCommon.Pause();
		}

		private void Main4(ArgsReader ar)
		{
			try
			{
				Main5(ar);
			}
			catch (Exception ex)
			{
				ProcMain.WriteLog(ex);

				MessageBox.Show(ex.ToString(), $"{Path.GetFileNameWithoutExtension(ProcMain.SelfFile)} / エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private int ServerPortNo = 80;
		private string GeminiAPIKey = "UNKNOWN";

		private void Main5(ArgsReader ar)
		{
			EventWaitHandle stopSvrEv = new EventWaitHandle(false, EventResetMode.ManualReset, Consts.STOP_SERVER_EVENT_NAME);

			for (; ; )
			{
				if (ar.ArgIs("/P"))
				{
					ServerPortNo = int.Parse(ar.NextArg());
					continue;
				}
				if (ar.ArgIs("/K"))
				{
					GeminiAPIKey = ar.NextArg();
					continue;
				}
				if (ar.ArgIs("/S"))
				{
					stopSvrEv.Set();
					return;
				}
				break;
			}
			ar.End();

			// -- 引数チェック --

			if (ServerPortNo < 1 || 65535 < ServerPortNo)
				throw new Exception("Bad ServerPortNo");

			if (GeminiAPIKey == "" || GeminiAPIKey.Any(ch => ch <= ' ' || '\x7e' < ch))
				throw new Exception("Bad GeminiAPIKey");

			// --

			HTTPServer hs = new HTTPServer()
			{
				PortNo = ServerPortNo,
				Backlog = 300,
				ConnectMax = 20,
				Interlude = () => !stopSvrEv.WaitOne(0),
				HTTPConnected = channel =>
				{
					// ====
					// 使い方メモ：

					// 以下は安全に表示可能な文字列であることが保証される。
					// -- FirstLine == ASCII && not-null
					// -- Method == ASCII && not-null
					// -- PathQuery == SJIS && not-null
					// -- HTTPVersion == ASCII && not-null
					// -- HeaderPairs == not-null && (全てのキーと値について ASCII && not-null)
					// ---- ASCII == [\u0020-\u007e]*
					// ---- SJIS == ToJString(, true, false, false, true)
					// 以下も保証される。
					// -- Body == not-null

					//Console.WriteLine(channel.FirstLine);
					//Console.WriteLine(channel.Method);
					//Console.WriteLine(channel.PathQuery);
					//Console.WriteLine(channel.HTTPVersion);
					//Console.WriteLine(string.Join(", ", channel.HeaderPairs.Select(pair => pair[0] + "=" + pair[1])));
					//Console.WriteLine(BitConverter.ToString(channel.Body.ToByteArray()));

					//channel.ResStatus = 200;
					//channel.ResHeaderPairs.Add(new string[] { "Content-Type", "text/plain; charset=US-ASCII" });
					//channel.ResHeaderPairs.Add(new string[] { "X-ResHeader-001", "123" });
					//channel.ResHeaderPairs.Add(new string[] { "X-ResHeader-002", "ABC" });
					//channel.ResHeaderPairs.Add(new string[] { "X-ResHeader-003", "abc" });
					//channel.ResBody = new byte[][] { Encoding.ASCII.GetBytes("Hello, Happy World!") };
					//channel.ResBodyLength = -1L;

					// ====

					ProcessRequest(channel);
				},
			};

			SockChannel.ThreadTimeoutMillis = 100;

			HTTPServer.KeepAliveTimeoutMillis = 5_000;

			HTTPServerChannel.RequestTimeoutMillis = -1;
			HTTPServerChannel.ResponseTimeoutMillis = -1;
			HTTPServerChannel.FirstLineTimeoutMillis = 2_000; // 2 sec
			HTTPServerChannel.IdleTimeoutMillis = 180_000; // 3 min
			HTTPServerChannel.BodySizeMax = 50_000_000; // 50 MB
			HTTPServerChannel.BodyOnStorage = false;

			SockCommon.TimeWaitMonitor.CTR_ROT_SEC = 60;
			SockCommon.TimeWaitMonitor.COUNTER_NUM = 5;
			SockCommon.TimeWaitMonitor.COUNT_LIMIT = 10000;

			hs.Run();
		}

		private string[] GlobalCandidateTokuchouArr = SCommon.EMPTY_STRINGS;

		private void ProcessRequest(HTTPServerChannel channel)
		{
			if (channel.PathQuery.ContainsIgnoreCase("sommelier-selection"))
			{
				byte[] encodedBText = channel.Body.ToByteArray();
				string text = DecodeURL(encodedBText);

				// "payload=" を除去
				{
					int p = text.IndexOf('=');

					if (p == -1)
						throw null;

					text = text.Substring(p + 1);
				}

				var root = JsonTools.Load(text);

				string[] selectedTypes = ConditionFilter(root["selected_types"].Array, 50);
				string[] selectedTokuchouArr = ConditionFilter(root["selected_tokuchou"].Array, 1000);
				string[] candidateTokuchouArr = ConditionFilter(root["candidate_tokuchou"].Array, 1000);

				if (
					selectedTypes == null ||
					selectedTypes.Length == 0 ||
					selectedTokuchouArr == null ||
					selectedTokuchouArr.Length == 0
					)
					throw new Exception("Bad request");

				GlobalCandidateTokuchouArr = candidateTokuchouArr.Where(v => !GlobalCandidateTokuchouArr.Contains(v))
					.Concat(GlobalCandidateTokuchouArr)
					.ToArray();

				string resHtml = SommelierService.GetSommelierSelectionHTML(
					GeminiAPIKey,
					selectedTypes,
					selectedTokuchouArr
					);

				channel.ResStatus = 200;
				channel.ResHeaderPairs.Add(new string[] { "Content-Type", "text/html; charset=UTF-8" });
				channel.ResBody = new byte[][] { Encoding.UTF8.GetBytes(resHtml) };
				channel.ResBodyLength = -1L;
			}
			else
			{
				string[] lines = File.ReadAllLines(Path.Combine(Consts.RES_DIR, "index.html"), Encoding.UTF8);

				HTMLReplaceList(
					ref lines,
					"<!-- CANDIDATE-TOKUCHOU-BEGIN -->",
					"<!-- CANDIDATE-TOKUCHOU-END -->",
					GlobalCandidateTokuchouArr,
					item => $"<span class=\"pill\">{item}</span>"
					);

				channel.ResStatus = 200;
				channel.ResHeaderPairs.Add(new string[] { "Content-Type", "text/html; charset=UTF-8" });
				channel.ResBody = new byte[][] { Encoding.UTF8.GetBytes(SCommon.LinesToText(lines)) };
				channel.ResBodyLength = -1L;
			}
		}

		private string DecodeURL(byte[] src)
		{
			using (MemoryStream writer = new MemoryStream())
			{
				for (int index = 0; index < src.Length; index++)
				{
					if (src[index] == 0x25 && index + 2 <= src.Length) // ? '%'
					{
						writer.WriteByte((byte)Convert.ToInt32(Encoding.ASCII.GetString(SCommon.GetPart(src, index + 1, 2)), 16));
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

				return SCommon.UTF8Conv.ToJString(bytes);
			}
		}

		private string[] ConditionFilter(List<JsonTools.Node> nodes, int countMax)
		{
			string[] items = nodes
				.Select(node => node.StringValue)
				.Select(item => SCommon.ToJString(item, true, false, false, true).Trim())
				.Where(item => item != "")
				.ToArray();

			if (items.Length > countMax)
				items = items.Take(countMax).ToArray();

			return items;
		}

		private void HTMLReplaceList(ref string[] lines, string beginLine, string endLine, string[] items, Func<string, string> itemToLine) // items == 空 -> 何もしない。
		{
			if (
				items == null ||
				items.Length == 0
				)
				return;

			int beginIndex = lines.IndexOfIgnoreCase(beginLine);
			if (beginIndex == -1)
				throw new Exception("no beginLine");

			int endIndex = lines.IndexOfIgnoreCase(endLine, beginIndex + 1);
			if (endIndex == -1)
				throw new Exception("no endLine");

			lines = lines.Take(beginIndex + 1)
				.Concat(items.Select(itemToLine))
				.Concat(lines.Skip(endIndex))
				.ToArray();
		}
	}
}
