using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace HLTStudio.Commons
{
	public static class ProcMain
	{
		public static string SelfFile;
		public static string SelfDir;

		public static ArgsReader ArgsReader;

		public static Action<object> WriteLog = message => { };

		public static void CUIMain(Action<ArgsReader> mainFunc)
		{
			try
			{
				WriteLog = message => Console.WriteLine("[" + SimpleDateTime.Now + "] " + message);

				SelfFile = Assembly.GetEntryAssembly().Location;
				SelfDir = Path.GetDirectoryName(SelfFile);

				WorkingDir.Root = new WorkingDir.RootInfo();

				ArgsReader = GetArgsReader();

				mainFunc(ArgsReader);

				WorkingDir.Root.Delete();
				WorkingDir.Root = null;
			}
			catch (Exception ex)
			{
				WriteLog(ex);

				MessageBox.Show(ex.ToString(), "HLTConsole / Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private static ArgsReader GetArgsReader()
		{
			return new ArgsReader(Environment.GetCommandLineArgs(), 1);
		}

		public static bool DEBUG
		{
			get
			{
#if DEBUG
				return true;
#else
				return false;
#endif
			}
		}
	}
}
