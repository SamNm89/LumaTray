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
            // Enable High DPI Awareness for crisp fonts & icons
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch { }

            // Ensure only one instance of LumaTray runs at a time
            bool createdNew;
            _appMutex = new Mutex(true, "Global\\LumaTray_SingleInstance_Mutex_v1", out createdNew);

            if (!createdNew)
            {
                // Already running
                MessageBox.Show("LumaTray is already running in your system tray.", "LumaTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new TrayApplicationContext());

            // Release mutex on exit
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Close();
            }
        }
    }
}
