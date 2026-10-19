using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LumaTray
{
    public class MonitorInfo
    {
        public string Id { get; set; }
        public string FriendlyName { get; set; }
        public string DeviceName { get; set; }
        public bool IsWmi { get; set; }
        public IntPtr PhysicalHandle { get; set; }
        public uint MinBrightness { get; set; }
        public uint MaxBrightness { get; set; }
        public int CurrentBrightness { get; set; }
        public string WmiInstanceName { get; set; }
    }

    public class MonitorManager : IDisposable
    {
        private readonly List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private readonly object _lock = new object();
        private readonly Queue<KeyValuePair<MonitorInfo, int>> _workQueue = new Queue<KeyValuePair<MonitorInfo, int>>();
        private readonly AutoResetEvent _workSignal = new AutoResetEvent(false);
        private readonly SynchronizationContext _syncContext;
        private Thread _workerThread;
        private bool _running = true;

        private bool _isRefreshing = false;
        private bool _refreshQueued = false;
        private readonly object _refreshLock = new object();

        private readonly List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>> _allocatedHandles =
            new List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>>();

        public event Action<MonitorInfo, int> BrightnessChanged;
        public event Action MonitorsRefreshed;

        public MonitorManager()
        {
            _syncContext = SynchronizationContext.Current;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "LumaTray_BrightnessWorker"
            };
            _workerThread.Start();
            RefreshMonitors();
        }

        public List<MonitorInfo> GetMonitors()
        {
            lock (_lock)
            {
                return new List<MonitorInfo>(_monitors);
            }
        }

        public void SetBrightnessAsync(MonitorInfo monitor, int target)
        {
            if (target < (int)monitor.MinBrightness) target = (int)monitor.MinBrightness;
            if (target > (int)monitor.MaxBrightness) target = (int)monitor.MaxBrightness;

            monitor.CurrentBrightness = target;

            lock (_workQueue)
            {
                _workQueue.Enqueue(new KeyValuePair<MonitorInfo, int>(monitor, target));
            }
            _workSignal.Set();

            if (BrightnessChanged != null)
            {
                BrightnessChanged(monitor, target);
            }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                _workSignal.WaitOne(200);
                if (!_running) break;

                Dictionary<MonitorInfo, int> latestUpdates = new Dictionary<MonitorInfo, int>();
                lock (_workQueue)
                {
                    while (_workQueue.Count > 0)
                    {
                        var item = _workQueue.Dequeue();
                        latestUpdates[item.Key] = item.Value;
                    }
                }

                foreach (var pair in latestUpdates)
                {
                    var mon = pair.Key;
                    int val = pair.Value;
                    try
                    {
                        if (mon.IsWmi)
                        {
                            SetWmiBrightness(mon.WmiInstanceName, val);
                        }
                        else
                        {
                            bool success = false;
                            for (int attempt = 0; attempt < 2; attempt++)
                            {
                                if (NativeMethods.SetVCPFeature(mon.PhysicalHandle, 0x10, (uint)val))
                                {
                                    success = true;
                                    break;
                                }
                                Thread.Sleep(15);
                            }
                            if (!success)
                            {
                                NativeMethods.SetMonitorBrightness(mon.PhysicalHandle, (uint)val);
                            }
                        }

                        // Debounced persistence runs on background worker thread
                        if (!string.IsNullOrEmpty(mon.Id))
                        {
                            SettingsManager.SaveBrightness(mon.Id, val);
                        }
                        if (!string.IsNullOrEmpty(mon.FriendlyName))
                        {
                            SettingsManager.SaveBrightness(mon.FriendlyName, val);
                        }
                    }
                    catch { }
                }
            }
        }

        public void RefreshMonitors()
        {
            lock (_refreshLock)
            {
                if (_isRefreshing)
                {
                    _refreshQueued = true;
                    return;
                }
                _isRefreshing = true;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                while (_running)
                {
                    lock (_refreshLock)
                    {
                        _refreshQueued = false;
                    }

                    PerformHardwareScan();

                    lock (_refreshLock)
                    {
                        if (!_refreshQueued)
                        {
                            _isRefreshing = false;
                            break;
                        }
                    }
                }
            });
        }

        private void PerformHardwareScan()
        {
            var newMonitors = new List<MonitorInfo>();
            var newHandles = new List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>>();

            // 1. Discover DDC/CI Physical Monitors
            int displayIndex = 1;
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data)
            {
                NativeMethods.MONITORINFOEX info = new NativeMethods.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                NativeMethods.GetMonitorInfo(hMon, ref info);

                string friendlyName, hardwareId, serial;
                GetDeviceInfoFromDevice(info.szDevice, out friendlyName, out hardwareId, out serial);

                uint count = 0;
                if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) && count > 0)
                {
                    var phys = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMon, count, phys))
                    {
                        newHandles.Add(new KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>(count, phys));

                        for (int i = 0; i < count; i++)
                        {
                            uint min = 0, curr = 50, max = 100;
                            uint vcpType = 0, vcpCur = 0, vcpMax = 0;
                            bool readOk = false;

                            // Resilient retry for DDC reads
                            for (int attempt = 0; attempt < 2; attempt++)
                            {
                                readOk = NativeMethods.GetVCPFeatureAndVCPFeatureReply(phys[i].hPhysicalMonitor, 0x10, out vcpType, out vcpCur, out vcpMax);
                                if (readOk && vcpMax > 0)
                                {
                                    min = 0;
                                    curr = vcpCur;
                                    max = vcpMax;
                                    break;
                                }
                                readOk = NativeMethods.GetMonitorBrightness(phys[i].hPhysicalMonitor, out min, out curr, out max);
                                if (readOk) break;

                                Thread.Sleep(15);
                            }

                            string name = friendlyName;
                            if (string.IsNullOrEmpty(name) || name == "Generic PnP Monitor")
                            {
                                name = !string.IsNullOrEmpty(phys[i].szPhysicalMonitorDescription) && phys[i].szPhysicalMonitorDescription != "Generic PnP Monitor"
                                    ? phys[i].szPhysicalMonitorDescription
                                    : "Display " + displayIndex;
                            }

                            // Generate unique hardware key (EDID serial > PNP hardware ID > device index)
                            string uniqueId;
                            if (!string.IsNullOrEmpty(serial))
                                uniqueId = name + "_" + serial;
                            else if (!string.IsNullOrEmpty(hardwareId))
                                uniqueId = hardwareId + "_" + i;
                            else
                                uniqueId = info.szDevice + "_" + displayIndex + "_" + i;

                            if (readOk)
                            {
                                SettingsManager.SaveBrightness(uniqueId, (int)curr);
                                SettingsManager.SaveBrightness(name, (int)curr);
                            }
                            else
                            {
                                int savedBrightness;
                                if (SettingsManager.TryGetBrightness(uniqueId, out savedBrightness) ||
                                    SettingsManager.TryGetBrightness(name, out savedBrightness))
                                {
                                    curr = (uint)savedBrightness;
                                }
                            }

                            // Keep monitor visible even if initial read timed out
                            newMonitors.Add(new MonitorInfo
                            {
                                Id = uniqueId,
                                FriendlyName = name,
                                DeviceName = info.szDevice,
                                IsWmi = false,
                                PhysicalHandle = phys[i].hPhysicalMonitor,
                                MinBrightness = min,
                                MaxBrightness = max > 0 ? max : 100,
                                CurrentBrightness = (int)curr
                            });
                        }
                    }
                }
                displayIndex++;
                return true;
            }, IntPtr.Zero);

            // 2. Discover WMI Internal Displays (Laptops, Surface, etc.) - Leak Free
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string instanceName = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : "Internal Display";
                            int curr = 50;
                            try { curr = Convert.ToInt32(mo["CurrentBrightness"]); } catch { }

                            string cleanName = "Internal Display";
                            if (instanceName.Contains(@"\"))
                            {
                                var parts = instanceName.Split('\\');
                                if (parts.Length > 1) cleanName = parts[1];
                            }

                            string wmiId = "WMI_" + instanceName;
                            int savedBrightness;
                            if (SettingsManager.TryGetBrightness(wmiId, out savedBrightness) ||
                                SettingsManager.TryGetBrightness(cleanName, out savedBrightness))
                            {
                                curr = savedBrightness;
                            }
                            else
                            {
                                SettingsManager.SaveBrightness(wmiId, curr);
                                SettingsManager.SaveBrightness(cleanName, curr);
                            }

                            newMonitors.Add(new MonitorInfo
                            {
                                Id = wmiId,
                                FriendlyName = cleanName,
                                DeviceName = "Built-in Screen",
                                IsWmi = true,
                                PhysicalHandle = IntPtr.Zero,
                                MinBrightness = 0,
                                MaxBrightness = 100,
                                CurrentBrightness = curr,
                                WmiInstanceName = instanceName
                            });
                        }
                    }
                }
            }
            catch { }

            // 3. Atomically swap monitors and physical handles
            List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>> oldHandles;
            lock (_lock)
            {
                oldHandles = new List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>>(_allocatedHandles);
                _allocatedHandles.Clear();
                _allocatedHandles.AddRange(newHandles);

                _monitors.Clear();
                _monitors.AddRange(newMonitors);
            }

            // 4. Safely clean up old physical handles on background thread
            foreach (var item in oldHandles)
            {
                try
                {
                    NativeMethods.DestroyPhysicalMonitors(item.Key, item.Value);
                }
                catch { }
            }

            // 5. Notify UI thread via SynchronizationContext
            if (_syncContext != null)
            {
                _syncContext.Post(delegate
                {
                    if (MonitorsRefreshed != null) MonitorsRefreshed();
                }, null);
            }
            else if (MonitorsRefreshed != null)
            {
                MonitorsRefreshed();
            }
        }

        private void SetWmiBrightness(string instanceName, int brightness)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string inst = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : "";
                            if (string.IsNullOrEmpty(instanceName) || inst == instanceName || inst.StartsWith(instanceName))
                            {
                                mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)brightness });
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void GetDeviceInfoFromDevice(string deviceName, out string friendlyName, out string hardwareId, out string serial)
        {
            friendlyName = null;
            hardwareId = null;
            serial = null;

            NativeMethods.DISPLAY_DEVICE dev = new NativeMethods.DISPLAY_DEVICE();
            dev.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));

            if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref dev, 0))
            {
                if (!string.IsNullOrEmpty(dev.DeviceID))
                {
                    hardwareId = dev.DeviceID;
                }

                string[] parts = (dev.DeviceID ?? "").Split('\\');
                if (parts.Length >= 2)
                {
                    string hwId = parts[1];
                    try
                    {
                        using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + hwId))
                        {
                            if (baseKey != null)
                            {
                                foreach (string subName in baseKey.GetSubKeyNames())
                                {
                                    using (RegistryKey devParam = baseKey.OpenSubKey(subName + @"\Device Parameters"))
                                    {
                                        if (devParam != null)
                                        {
                                            byte[] edid = devParam.GetValue("EDID") as byte[];
                                            if (edid != null && edid.Length >= 128)
                                            {
                                                if (string.IsNullOrEmpty(friendlyName))
                                                {
                                                    friendlyName = ParseEdidName(edid);
                                                }
                                                if (string.IsNullOrEmpty(serial))
                                                {
                                                    serial = ParseEdidSerial(edid);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(friendlyName) && !string.IsNullOrEmpty(dev.DeviceString) && dev.DeviceString != "Generic PnP Monitor")
                {
                    friendlyName = dev.DeviceString;
                }
            }
        }

        private static string ParseEdidName(byte[] edid)
        {
            int[] offsets = new int[] { 54, 72, 90, 108 };
            foreach (int off in offsets)
            {
                if (off + 18 <= edid.Length)
                {
                    if (edid[off] == 0 && edid[off + 1] == 0 && edid[off + 2] == 0 && edid[off + 3] == 0xFC)
                    {
                        string name = Encoding.ASCII.GetString(edid, off + 5, 13).Trim('\0', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }
            }
            return null;
        }

        private static string ParseEdidSerial(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return null;
            int[] offsets = new int[] { 54, 72, 90, 108 };
            foreach (int off in offsets)
            {
                if (off + 18 <= edid.Length)
                {
                    if (edid[off] == 0 && edid[off + 1] == 0 && edid[off + 2] == 0 && edid[off + 3] == 0xFF)
                    {
                        string serial = Encoding.ASCII.GetString(edid, off + 5, 13).Trim('\0', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(serial))
                            return serial;
                    }
                }
            }
            return null;
        }

        private void CleanupPhysicalHandles()
        {
            lock (_lock)
            {
                foreach (var item in _allocatedHandles)
                {
                    try
                    {
                        NativeMethods.DestroyPhysicalMonitors(item.Key, item.Value);
                    }
                    catch { }
                }
                _allocatedHandles.Clear();
            }
        }

        public void Dispose()
        {
            _running = false;
            _workSignal.Set();
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(500);
            }
            lock (_lock)
            {
                SettingsManager.SaveAllBrightness(_monitors);
            }
            CleanupPhysicalHandles();
            _workSignal.Close();
        }
    }
}
