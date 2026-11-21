using System;
using System.Text;
using System.Windows.Forms;
using AmazingBot.Native;

namespace AmazingBot.App
{
    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // WinExe — консоли нет, это нормально
            }

            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch (Exception)
            {
                // старые Windows / недостаточно прав — продолжим
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (sender, args) =>
            {
                MessageBox.Show(
                    "Необработанная ошибка:\n" + args.Exception.Message,
                    "AmazingBot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
            return 0;
        }
    }
}
