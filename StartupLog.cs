using System;
using System.Diagnostics;
using System.IO;

namespace LumaTray
{
    /// <summary>
    /// Lightweight startup/error trace so a slow or failed launch can be diagnosed
    /// after the fact. Appends to %LOCALAPPDATA%\LumaTray\startup.log (rotated at 64 KB).
    /// </summary>
    public static class StartupLog
    {
        private static readonly object _lock = new object();
        private static readonly Stopwatch _sinceFirstUse = Stopwatch.StartNew();
        private static string _path;
        private static bool _ready;

        public static string FilePath
        {
            get
            {
                EnsureReady();
                return _path;
            }
        }

        /// <summary>Milliseconds from process creation to now (includes loader + JIT).</summary>
        public static long SinceProcessStartMs
        {
            get
            {
                try { return (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
                catch { return -1; }
            }
        }

        private static string ProcessStartText()
        {
            try { return Process.GetCurrentProcess().StartTime.ToString("HH:mm:ss.fff"); }
            catch { return "?"; }
        }

        private static void EnsureReady()
        {
            if (_ready) return;
            lock (_lock)
            {
                if (_ready) return;
                _ready = true;
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaTray");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    _path = Path.Combine(dir, "startup.log");
                    try
                    {
                        FileInfo fi = new FileInfo(_path);
                        if (fi.Exists && fi.Length > 64 * 1024)
                        {
                            string old = _path + ".1";
                            try { if (File.Exists(old)) File.Delete(old); } catch { }
                            File.Move(_path, old);
                        }
                    }
                    catch { }

                    File.AppendAllText(_path,
                        "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                        "  pid " + Process.GetCurrentProcess().Id +
                        "  process start " + ProcessStartText() +
                        "  uptime " + ((uint)Environment.TickCount / 1000) + " s ===" + Environment.NewLine);
                }
                catch { _path = null; }
            }
        }

        public static void Write(string message)
        {
            EnsureReady();
            if (_path == null) return;
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_path,
                        "  +" + _sinceFirstUse.ElapsedMilliseconds.ToString().PadLeft(5) + " ms  " + message + "\r\n");
                }
                catch { }
            }
        }
    }
}
