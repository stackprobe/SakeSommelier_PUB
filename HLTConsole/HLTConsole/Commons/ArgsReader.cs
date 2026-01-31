using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace HLTStudio.Commons
{
	public class ArgsReader
	{
		private string[] Args = null;
		private int ArgIndex;

		public ArgsReader(string[] args, int argIndex = 0)
		{
			this.Args = args;
			this.ArgIndex = argIndex;
		}

		public bool HasArgs(int count = 1)
		{
			return count <= this.Args.Length - this.ArgIndex;
		}

		public bool ArgIs(string spell)
		{
			if (this.HasArgs() && this.GetArg().EqualsIgnoreCase(spell))
			{
				this.ArgIndex++;
				return true;
			}
			return false;
		}

		public string GetArg(int index = 0)
		{
			return this.Args[this.ArgIndex + index];
		}

		public string NextArg()
		{
			string arg = this.GetArg();
			this.ArgIndex++;
			return arg;
		}

		public IEnumerable<string> TrailArgs()
		{
			while (this.HasArgs())
				yield return this.NextArg();
		}

		/// <summary>
		/// コマンド引数を読み終えたら呼ぶこと。(任意)
		/// コマンド引数の誤指定対策として、コマンド引数が余っていたら例外を投げる。
		/// </summary>
		public void End()
		{
			if (this.HasArgs())
				throw new Exception("Bad command line option-num");
		}
	}
}
