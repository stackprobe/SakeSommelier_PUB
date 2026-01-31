using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HLTStudio.Commons
{
	public abstract class Randomizer
	{
		protected abstract byte[] GetBlock();

		private byte[] Cache = SCommon.EMPTY_BYTES;
		private int NextRdIndex = 0;

		private byte GetByte()
		{
			if (this.Cache.Length <= this.NextRdIndex)
			{
				byte[] block = this.GetBlock();

				if (
					block == null ||
					block.Length == 0
					)
					throw new Exception("Bad block");

				this.Cache = block;
				this.NextRdIndex = 0;
			}
			return this.Cache[this.NextRdIndex++];
		}

		private int Bits;
		private int BitPos = 8;

		private int GetBit()
		{
			if (8 <= this.BitPos)
			{
				this.Bits = this.GetByte();
				this.BitPos = 0;
			}
			return (this.Bits >> this.BitPos++) & 1;
		}

		public byte[] GetBytes(int length)
		{
			byte[] dest = new byte[length];

			for (int index = 0; index < length; index++)
				dest[index] = this.GetByte();

			return dest;
		}

		public uint GetUInt8()
		{
			return (uint)this.GetByte();
		}

		public uint GetUInt16()
		{
			byte[] r = GetBytes(2);

			return
				((uint)r[0] << 0) |
				((uint)r[1] << 8);
		}

		public uint GetUInt24()
		{
			byte[] r = GetBytes(3);

			return
				((uint)r[0] << 0) |
				((uint)r[1] << 8) |
				((uint)r[2] << 16);
		}

		public uint GetUInt32()
		{
			byte[] r = GetBytes(4);

			return
				((uint)r[0] << 0) |
				((uint)r[1] << 8) |
				((uint)r[2] << 16) |
				((uint)r[3] << 24);
		}

		public ulong GetULong40()
		{
			byte[] r = GetBytes(5);

			return
				((ulong)r[0] << 0) |
				((ulong)r[1] << 8) |
				((ulong)r[2] << 16) |
				((ulong)r[3] << 24) |
				((ulong)r[4] << 32);
		}

		public ulong GetULong48()
		{
			byte[] r = GetBytes(6);

			return
				((ulong)r[0] << 0) |
				((ulong)r[1] << 8) |
				((ulong)r[2] << 16) |
				((ulong)r[3] << 24) |
				((ulong)r[4] << 32) |
				((ulong)r[5] << 40);
		}

		public ulong GetULong56()
		{
			byte[] r = GetBytes(7);

			return
				((ulong)r[0] << 0) |
				((ulong)r[1] << 8) |
				((ulong)r[2] << 16) |
				((ulong)r[3] << 24) |
				((ulong)r[4] << 32) |
				((ulong)r[5] << 40) |
				((ulong)r[6] << 48);
		}

		public ulong GetULong64()
		{
			byte[] r = GetBytes(8);

			return
				((ulong)r[0] << 0) |
				((ulong)r[1] << 8) |
				((ulong)r[2] << 16) |
				((ulong)r[3] << 24) |
				((ulong)r[4] << 32) |
				((ulong)r[5] << 40) |
				((ulong)r[6] << 48) |
				((ulong)r[7] << 56);
		}

		public ulong GetULong(ulong modulo)
		{
			if (modulo == 0)
				throw new Exception("Bad modulo");

			ulong t = (~modulo + 1) % modulo;
			ulong r;

			do
			{
				r = this.GetULong64();
			}
			while (r < t);

			r %= modulo;

			return r;
		}

		public uint GetUInt(uint modulo)
		{
			return (uint)this.GetULong((ulong)modulo);
		}

		public long GetLong(long modulo)
		{
			return (long)this.GetULong((ulong)modulo);
		}

		public int GetInt(int modulo)
		{
			return (int)this.GetULong((ulong)modulo);
		}

		public int GetRange(int minval, int maxval)
		{
			return this.GetInt(maxval - minval + 1) + minval;
		}

		public long GetLongRange(long minval, long maxval)
		{
			return this.GetLong(maxval - minval + 1) + minval;
		}

		public bool GetBoolean()
		{
			return this.GetBit() != 0;
		}

		public int GetSign()
		{
			return this.GetBit() * 2 - 1;
		}

		public double GetRate()
		{
			return (double)this.GetULong48() / ((1UL << 48) - 1);
		}

		public double GetDoubleRange(double minval, double maxval)
		{
			return this.GetRate() * (maxval - minval) + minval;
		}

		public T ChooseOne<T>(IList<T> list)
		{
			return list[this.GetInt(list.Count)];
		}

		public void Shuffle<T>(IList<T> list)
		{
			for (int index = list.Count; 1 < index; index--)
			{
				SCommon.Swap(list, this.GetInt(index), index - 1);
			}
		}
	}
}
