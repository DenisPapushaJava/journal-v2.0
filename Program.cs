
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Jornal
{
	
	internal sealed class Program
	{	
		[STAThread]
		private static void Main(string[] args)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.ThreadException += new ThreadExceptionEventHandler(Exception);
			Application.Run(new MainForm());
		}

         static void Exception(object sender, ThreadExceptionEventArgs e)
        {
            File.AppendAllText("error.log", $"[{DateTime.Now}] {e.Exception}\n\n");
            MessageBox.Show(e.Exception.ToString(),"Ошибка",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}
