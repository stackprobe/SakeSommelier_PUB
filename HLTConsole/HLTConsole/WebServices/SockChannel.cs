using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using HLTStudio.Commons;

namespace HLTStudio.WebServices
{
	public class SockChannel
	{
		public Socket Handler;
		public int ID;
		public Func<int> Connected;
		public HTTPBodyOutputStream.IBOS BodyOutputStream;
		public SockServer Parent;

		// <---- need init

		/// <summary>
		/// スレッド占用タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public static int ThreadTimeoutMillis = 100;

		// <---- init if needed

		public bool FirstLineRecving = false;

		/// <summary>
		/// セッションタイムアウト日時
		/// null == INFINITE
		/// </summary>
		public DateTime? SessionTimeoutTime = null;

		/// <summary>
		/// スレッド占用タイムアウト日時
		/// null == リセット状態
		/// </summary>
		public DateTime? ThreadTimeoutTime = null;

		/// <summary>
		/// 無通信タイムアウト_ミリ秒
		/// -1 == INFINITE
		/// </summary>
		public int CurrIdleTimeoutMillis = -1;

		private IEnumerable<int> PreRecvSend()
		{
			if (this.SessionTimeoutTime != null && this.SessionTimeoutTime.Value < DateTime.Now)
			{
				throw new Exception("セッション時間切れ");
			}
			if (this.ThreadTimeoutTime == null)
			{
				if (ThreadTimeoutMillis != -1)
					this.ThreadTimeoutTime = DateTime.Now + TimeSpan.FromMilliseconds((double)ThreadTimeoutMillis);
			}
			else if (this.ThreadTimeoutTime.Value < DateTime.Now)
			{
				//SockCommon.WriteLog(SockCommon.ErrorLevel_e.INFO, "スレッド占用タイムアウト"); // 頻出するので抑止

				this.ThreadTimeoutTime = null;
				yield return -1;
			}
		}

		public IEnumerable<int> Recv(int size, Action<byte[]> a_return)
		{
			byte[] data = new byte[size];
			int offset = 0;

			while (1 <= size)
			{
				int? recvSize = null;

				foreach (var relay in this.TryRecv(data, offset, size, ret => recvSize = ret))
					yield return relay;

				size -= recvSize.Value;
				offset += recvSize.Value;
			}
			a_return(data);
		}

		public IEnumerable<int> TryRecv(byte[] data, int offset, int size, Action<int> a_return)
		{
			DateTime startedTime = DateTime.Now;

			for (; ; )
			{
				foreach (var relay in this.PreRecvSend())
					yield return relay;

				try
				{
					int recvSize = SockCommon.NB("recv", () => this.Handler.Receive(data, offset, size, SocketFlags.None));

					if (recvSize <= 0)
					{
						throw new Exception("受信切断");
					}
					if (10.0 <= (DateTime.Now - startedTime).TotalSeconds) // 長い無通信時間をモニタする。
					{
						SockCommon.WriteLog(SockCommon.ErrorLevel_e.WARNING, $"IDLE-RECV {(DateTime.Now - startedTime).TotalSeconds:F3}");
					}
					a_return(recvSize);
					break;
				}
				catch (SocketException ex)
				{
					if (ex.ErrorCode != SockCommon.WSAEWOULDBLOCK)
					{
						throw new Exception($"受信切断({ex.ErrorCode})", ex);
					}
				}
				if (this.CurrIdleTimeoutMillis != -1 && this.CurrIdleTimeoutMillis < (DateTime.Now - startedTime).TotalMilliseconds)
				{
					throw new RecvIdleTimeoutException();
				}
				this.ThreadTimeoutTime = null;
				yield return -1;
			}
			yield return 1;
		}

		/// <summary>
		/// 受信の無通信タイムアウト
		/// </summary>
		public class RecvIdleTimeoutException : Exception
		{ }

		public IEnumerable<int> Send(byte[] data)
		{
			int offset = 0;
			int size = data.Length;

			while (1 <= size)
			{
				//int vsDenom = this.Parent.ChannelCount;
				//vsDenom = Math.Max(1, vsDenom); // 2bs

				int vSize = 4000000;
				//vSize /= vsDenom;
				//vSize = Math.Max(vSize, 1000); // 2bs
				vSize = Math.Min(vSize, size);

				int? sentSize = null;

				foreach (var relay in this.TrySend(data, offset, vSize, ret => sentSize = ret))
					yield return relay;

				size -= sentSize.Value;
				offset += sentSize.Value;
			}
		}

		private IEnumerable<int> TrySend(byte[] data, int offset, int size, Action<int> a_return)
		{
			DateTime startedTime = DateTime.Now;

			for (; ; )
			{
				foreach (var relay in this.PreRecvSend())
					yield return relay;

				try
				{
					int sentSize = SockCommon.NB("send", () => this.Handler.Send(data, offset, size, SocketFlags.None));

					if (sentSize <= 0)
					{
						throw new Exception("送信切断");
					}
					if (10.0 <= (DateTime.Now - startedTime).TotalSeconds) // 長い無通信時間をモニタする。
					{
						SockCommon.WriteLog(SockCommon.ErrorLevel_e.WARNING, $"IDLE-SEND {(DateTime.Now - startedTime).TotalSeconds:F3}");
					}
					a_return(sentSize);
					break;
				}
				catch (SocketException ex)
				{
					if (ex.ErrorCode != SockCommon.WSAEWOULDBLOCK)
					{
						throw new Exception($"送信切断({ex.ErrorCode})", ex);
					}
				}
				if (this.CurrIdleTimeoutMillis != -1 && this.CurrIdleTimeoutMillis < (DateTime.Now - startedTime).TotalMilliseconds)
				{
					throw new Exception("送信の無通信タイムアウト");
				}
				this.ThreadTimeoutTime = null;
				yield return -1;
			}
			yield return 1;
		}
	}
}
