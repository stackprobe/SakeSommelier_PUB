using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HLTStudio.Commons;

namespace HLTStudio
{
	public static class Consts
	{
		public static readonly string STOP_SERVER_EVENT_NAME = "HLT_SakeSommelier_{64a8a42f-303e-40c8-bac8-382e918db57e}_STOP-SERVER-EVENT";

		public static string RES_DIR
		{
			get
			{
				string dir = Path.Combine(ProcMain.SelfDir, "res");

				if (!Directory.Exists(dir))
				{
					dir = @"..\..\..\..\doc\res";

					if (!Directory.Exists(dir))
						throw new Exception("no res dir");
				}
				return dir;
			}
		}
	}
}
