using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using HLTStudio.Commons;
using HLTStudio.Tools;

namespace HLTStudio.WebServices
{
	public static class HTTPBodyOutputStream
	{
		public static IBOS Create(bool fileMode)
		{
			if (fileMode)
				return new FileBOS();
			else
				return new MemoryBOS();
		}

		public interface IBOS : IDisposable
		{
			/// <summary>
			/// バイト列を書き込む。
			/// </summary>
			/// <param name="data">書き込むバイト列</param>
			void Write(byte[] data);

			/// <summary>
			/// 書き込んだ総バイト数を返す。
			/// </summary>
			/// <returns>書き込んだ総バイト数</returns>
			long GetWroteSize();

			/// <summary>
			/// 書き込んだバイト列を返し、リセットする。
			/// </summary>
			/// <returns>書き込んだバイト列</returns>
			byte[] ToByteArray();

			/// <summary>
			/// 書き込んだバイト列をファイルに出力し、リセットする。
			/// </summary>
			/// <param name="destFile">出力ファイル</param>
			void ToFile(string destFile);

			/// <summary>
			/// 書き込んだバイト列を取得し、リセットする。
			/// </summary>
			/// <param name="writer">取得メソッド</param>
			void ReadToEnd(SCommon.Write_d writer);
		}

		private class FileBOS : IBOS
		{
			public WorkingDir WD = new WorkingDir();
			public string BufferFile;
			public long WroteSize = 0L;
			public CtrCipher CtrCipher = CtrCipher.CreateTemporary();

			public FileBOS()
			{
				this.BufferFile = this.WD.MakePath();
			}

			public void Write(byte[] data)
			{
				byte[] maskedPart = new byte[data.Length];

				this.CtrCipher.Mask(data, 0, maskedPart, 0, data.Length);

				using (FileStream writer = new FileStream(this.BufferFile, FileMode.Append, FileAccess.Write))
				{
					writer.Write(maskedPart, 0, data.Length);
				}
				this.WroteSize += data.Length;
			}

			public long GetWroteSize()
			{
				return this.WroteSize;
			}

			public byte[] ToByteArray()
			{
				if (this.WroteSize == 0L)
					return SCommon.EMPTY_BYTES;

				byte[] data = File.ReadAllBytes(this.BufferFile);
				SCommon.DeletePath(this.BufferFile);
				this.WroteSize = 0L;

				this.CtrCipher.Reset();
				this.CtrCipher.Mask(data);
				this.CtrCipher.Reset();

				return data;
			}

			public void ToFile(string destFile)
			{
				if (this.WroteSize == 0L)
				{
					File.WriteAllBytes(destFile, SCommon.EMPTY_BYTES);
					return;
				}
				this.CtrCipher.Reset();

				using (FileStream reader = new FileStream(this.BufferFile, FileMode.Open, FileAccess.Read))
				using (FileStream writer = new FileStream(destFile, FileMode.Create, FileAccess.Write))
				{
					SCommon.ReadToEnd(reader.Read, (buff, offset, count) =>
					{
						this.CtrCipher.Mask(buff, offset, count);
						writer.Write(buff, offset, count);
					});
				}

				SCommon.DeletePath(this.BufferFile);
				this.WroteSize = 0L;

				this.CtrCipher.Reset();
			}

			public void ReadToEnd(SCommon.Write_d writer)
			{
				if (this.WroteSize == 0L)
					return;

				this.CtrCipher.Reset();

				using (FileStream reader = new FileStream(this.BufferFile, FileMode.Open, FileAccess.Read))
				{
					SCommon.ReadToEnd(reader.Read, (buff, offset, count) =>
					{
						this.CtrCipher.Mask(buff, offset, count);
						writer(buff, offset, count);
					});
				}

				SCommon.DeletePath(this.BufferFile);
				this.WroteSize = 0;

				this.CtrCipher.Reset();
			}

			public void Dispose()
			{
				if (this.WD != null)
				{
					this.WD.Dispose();
					this.WD = null;

					this.CtrCipher.Dispose();
					this.CtrCipher = null;
				}
			}
		}

		private class MemoryBOS : IBOS
		{
			private MemoryStream Buffer = new MemoryStream();

			public void Write(byte[] data)
			{
				SCommon.Write(this.Buffer, data);
			}

			public long GetWroteSize()
			{
				return this.Buffer.Length;
			}

			public byte[] ToByteArray()
			{
				byte[] data = this.Buffer.ToArray();
				this.Buffer.SetLength(0L);
				return data;
			}

			public void ToFile(string destFile)
			{
				File.WriteAllBytes(destFile, this.ToByteArray());
			}

			public void ReadToEnd(SCommon.Write_d writer)
			{
				SCommon.Write(writer, this.ToByteArray());
			}

			public void Dispose()
			{
				if (this.Buffer != null)
				{
					this.Buffer.Dispose();
					this.Buffer = null;
				}
			}
		}
	}
}
