using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Countdown
{
    internal static class Program
    {
        const string MutexName = @"Local\Countdown.SingleInstance.6e1b0d47";

        [STAThread]
        static int Main(string[] args)
        {
            // Headless modes: no windows, no message loop.
            if (args.Contains("--check"))
            {
                UseStandardOutput();
                return SelfCheck.Run() ? 0 : 1;
            }
            if (args.Contains("--diagnose"))
            {
                UseStandardOutput();
                Diagnostics.Print();
                return 0;
            }

            Mutex? mutex = null;
            bool isFirstInstance = false;
            try
            {
                mutex = new Mutex(true, MutexName, out isFirstInstance);
            }
            catch (UnauthorizedAccessException)
            {
                // A copy running as administrator owns the mutex; this one may not open it.
            }
            if (!isFirstInstance)
            {
                // Launching it again opens the running copy's settings, like reopening on macOS.
                mutex?.Dispose();
                SignalRunningInstance();
                return 0;
            }
            using var ownedMutex = mutex;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("unhandled: " + e.Exception);
            using var app = new CountdownApp();
            Application.Run(app);
            return 0;
        }

        static void SignalRunningInstance()
        {
            IntPtr window = Native.FindWindow(null, MessageWindow.WindowTitle);
            if (window == IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(window, out uint processId);
            Native.AllowSetForegroundWindow(processId);
            Native.PostMessage(window, MessageWindow.ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// A Windows app has no console: print to a redirected stdout if there is one (CI),
        /// otherwise to the console it was started from. Always UTF-8.
        /// </summary>
        static void UseStandardOutput()
        {
            if (Native.GetStdHandle(Native.STD_OUTPUT_HANDLE) == IntPtr.Zero)
            {
                Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
            }
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
        }
    }
}
