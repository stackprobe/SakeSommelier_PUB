using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HLTStudio.WebServices
{
	// このクラスの使い方メモ：
	// -- https://github.com/stackprobe/Dev/blob/main/Barebone/_src/_ref/UsageExamples_Tools.cs#L36-L90

	public class HTTPServer : SockServer
	{
		/// <summary>
		/// サーバーロジック
		/// 引数：
		/// -- channel: 接続チャネル
		/// </summary>
		public Action<HTTPServerChannel> HTTPConnected = channel => { };

		/// <summary>
		/// キープアライブのタイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int KeepAliveTimeoutMillis = 5000;

		// <---- init if needed

		public HTTPServer()
		{
			PortNo = 80;
		}

		protected override IEnumerable<int> E_Connected(SockChannel channel)
		{
			DateTime startedTime = DateTime.Now;

			for (; ; )
			{
				HTTPServerChannel hsChannel = new HTTPServerChannel();
				int retval = -1;

				hsChannel.Channel = channel;

				foreach (int size in hsChannel.RecvRequest())
				{
					if (size == 0)
						throw null; // never

					if (size < 0)
					{
						yield return retval;
						retval = -1;
					}
					else
					{
						retval = 1;
					}
				}

				SockCommon.NB("svlg", () =>
				{
					HTTPConnected(hsChannel);
					return -1; // dummy
				});

				if (KeepAliveTimeoutMillis != -1 && KeepAliveTimeoutMillis < (DateTime.Now - startedTime).TotalMilliseconds)
				{
					hsChannel.KeepAlive = false;
				}

				foreach (int size in hsChannel.SendResponse())
				{
					if (size == 0)
						throw null; // never

					if (size < 0)
					{
						yield return retval;
						retval = -1;
					}
					else
					{
						retval = 1;
					}
				}

				if (!hsChannel.KeepAlive)
					break;
			}
		}
	}
}
