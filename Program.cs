using System;
using System.Threading;
using System.Windows.Forms;

namespace LumaTray
{
    static class Program
    {
        private static Mutex _appMutex;

        [STAThread]
        static void Main()
        {
            StartupLog.Write("Main entered (process start -> Main: " + StartupLog.SinceProcessStartMs + " ms)");

            // Exceptions on background threads (DDC/WMI/update workers) bypass
            // Application.ThreadException and silently terminate the process.
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                StartupLog.Write("FATAL unhandled exception: " + e.ExceptionObject);
            };

            try
            {
                RunApp();
            }
            catch (Exception ex)
            {
                StartupLog.Write("FATAL exception in Main: " + ex);
                try
                {
                    MessageBox.Show(
                        "LumaTray could not start:\n" + ex.Message +
                        "\n\nDetails were written to:\n" + StartupLog.FilePath,
                        "LumaTray Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private static void RunApp()
        {
            // Enable Per-Monitor V2 High DPI Awareness for crisp fonts & icons on mixed-DPI screens
            try
            {
                if (!NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    NativeMethods.SetProcessDPIAware();
                }
            }
            catch
            {
                try { NativeMethods.SetProcessDPIAware(); } catch { }
            }

            // Ensure only one instance of LumaTray runs at a time
            bool createdNew;
            _appMutex = new Mutex(true, "Global\\LumaTray_SingleInstance_Mutex_v1", out createdNew);

            if (!createdNew)
            {
                // Already running
                StartupLog.Write("another instance is already running, exiting");
                MessageBox.Show("LumaTray is already running in your system tray.", "LumaTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartupLog.Write("single-instance mutex acquired");

            // Clean up any renamed .old executable from a previous update
            try
            {
                string oldFile = Application.ExecutablePath + ".old";
                if (System.IO.File.Exists(oldFile)) System.IO.File.Delete(oldFile);
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                StartupLog.Write("unhandled UI-thread exception: " + e.Exception);
                try
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaTray");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash.log"), e.Exception.ToString());
                }
                catch { }
                MessageBox.Show("An unexpected error occurred in LumaTray:\n" + e.Exception.Message, "LumaTray Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            StartupLog.Write("creating tray context");
            TrayApplicationContext context = new TrayApplicationContext();
            StartupLog.Write("tray context created, entering message loop");

            Application.Run(context);

            StartupLog.Write("message loop exited");

            // Release mutex on exit
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Close();
            }
        }
    }
}
