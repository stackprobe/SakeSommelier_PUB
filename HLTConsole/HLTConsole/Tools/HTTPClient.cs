using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using HLTStudio.Commons;

namespace HLTStudio.Tools
{
	// このクラスの使い方メモ：
	// -- https://github.com/stackprobe/Dev/blob/main/Barebone/_src/_ref/UsageExamples_Tools.cs#L15-L34

	public class HTTPClient
	{
		private HttpWebRequest Inner;

		public HTTPClient(string url)
		{
			PrepareEnvironment();

			this.Inner = (HttpWebRequest)HttpWebRequest.Create(url);
			this.Inner.Proxy = null;
		}

		private static void PrepareEnvironment()
		{
			// どんな証明書も許可する。
			ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

			// TLS 1.2
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
		}

		/// <summary>
		/// 接続を試みてから応答ヘッダを受信し終えるまでのタイムアウト_ミリ秒
		/// </summary>
		public int ConnectTimeoutMillis = 43200000; // 12 hour

		/// <summary>
		/// 接続を試みてから全て送受信し終えるまでのタイムアウト_ミリ秒
		/// </summary>
		public int TimeoutMillis = 86400000; // 1 day

		/// <summary>
		/// 応答ヘッダを受信し終えてから全て送受信し終えるまでの間の無通信タイムアウト_ミリ秒
		/// </summary>
		public int IdleTimeoutMillis = 180000; // 3 min

		/// <summary>
		/// 応答ボディ最大サイズ_バイト数
		/// </summary>
		public long ResBodySizeMax = 8000000000000000; // 8 PB (8000 TB)

		/// <summary>
		/// 応答ボディ出力ファイル
		/// null == 出力しない。
		/// </summary>
		public string ResFile = null;

		public void AddHeader(string name, string value)
		{
			this.Inner.Headers.Add(name, value);
		}

		public void SetAuthorization(string user, string password)
		{
			string plain = user + ":" + password;
			string enc = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
			this.AddHeader("Authorization", "Basic " + enc);
		}

		public HttpWebRequest GetInner()
		{
			return this.Inner;
		}

		/// <summary>
		/// HEAD-リクエストを実行
		/// </summary>
		public void Head()
		{
			this.GetOrPost(null, "HEAD");
		}

		/// <summary>
		/// GET-リクエストを実行
		/// </summary>
		public void Get()
		{
			this.GetOrPost(null, "GET");
		}

		/// <summary>
		/// POST-リクエストを実行
		/// </summary>
		/// <param name="bodyFile">リクエストボディファイル</param>
		public void Post(string bodyFile)
		{
			this.GetOrPost(bodyFile, "POST");
		}

		private void GetOrPost(string bodyFile, string method)
		{
			ProcMain.WriteLog("HTTPClient-ST");

			DateTime timeoutTime = DateTime.Now + TimeSpan.FromMilliseconds((double)TimeoutMillis);

			// 2022.5.29
			// 送信ファイルをメモリに読み込まない。
			// これをしないと、送信ファイルをメモリに読み込んでから送信しようとする。-> でかいファイルでメモリ不足になる。
			{
				this.Inner.AllowWriteStreamBuffering = false;
			}

			this.Inner.Timeout = this.ConnectTimeoutMillis;
			this.Inner.Method = method;

			if (bodyFile != null)
			{
				ProcMain.WriteLog("HTTPClient-SendBody-ST");

				if (bodyFile == "")
					throw new Exception("Bad bodyFile");

				if (!File.Exists(bodyFile))
					throw new Exception("no bodyFile");

				this.Inner.ContentLength = new FileInfo(bodyFile).Length;

				using (Stream reader = new FileStream(bodyFile, FileMode.Open, FileAccess.Read))
				using (Stream writer = this.Inner.GetRequestStream())
				{
					SCommon.ReadToEnd(reader.Read, writer.Write);
				}

				ProcMain.WriteLog("HTTPClient-SendBody-ED");
			}
			using (WebResponse res = this.Inner.GetResponse())
			{
				this.ResHeaders = SCommon.CreateDictionaryIgnoreCase<string>();

				// 受信ヘッダ
				{
					const int WEIGHT = 256;
					const int RES_HEADER_LEN_MAX = 128 * 1024 + 256 * WEIGHT;

					int roughResHeaderLength = 0;

					foreach (string name in res.Headers.Keys)
					{
						if (RES_HEADER_LEN_MAX < name.Length)
							throw new Exception("受信ヘッダが長すぎます。");

						roughResHeaderLength += name.Length + WEIGHT;

						if (RES_HEADER_LEN_MAX < roughResHeaderLength)
							throw new Exception("受信ヘッダが長すぎます。");

						string value = res.Headers[name];

						if (RES_HEADER_LEN_MAX < value.Length)
							throw new Exception("受信ヘッダが長すぎます。");

						roughResHeaderLength += value.Length + WEIGHT;

						if (RES_HEADER_LEN_MAX < roughResHeaderLength)
							throw new Exception("受信ヘッダが長すぎます。");

						this.ResHeaders.Add(name, res.Headers[name]);
					}
				}

				// 受信ボディ
				{
					ProcMain.WriteLog("HTTPClient-RecvResBody-ST");

					long totalSize = 0L;
					long recvCount = 0L;

					FileStream writer =
						this.ResFile == null ?
							null :
							new FileStream(this.ResFile, FileMode.Create, FileAccess.Write);

					try
					{
						using (Stream reader = res.GetResponseStream())
						{
							reader.ReadTimeout = this.IdleTimeoutMillis; // この時間経過すると reader.Read() が例外を投げる。

							byte[] buff = new byte[2 * 1024 * 1024];

							for (; ; )
							{
								int readSize = reader.Read(buff, 0, buff.Length);

								if (readSize <= 0)
									break;

								if (timeoutTime < DateTime.Now)
									throw new Exception("受信タイムアウト");

								totalSize += (long)readSize;
								recvCount++;

								if (this.ResBodySizeMax < totalSize)
									throw new Exception("受信データが長すぎます。");

								if (writer != null)
									writer.Write(buff, 0, readSize);
							}
						}
					}
					finally
					{
						if (writer != null)
						{
							writer.Dispose();
							writer = null;
						}
					}

					ProcMain.WriteLog("HTTPClient-RecvResBody-ED " + totalSize + " (" + recvCount + ")");
				}
			}

			ProcMain.WriteLog("HTTPClient-ED");
		}

		public Dictionary<string, string> ResHeaders;
	}
}
