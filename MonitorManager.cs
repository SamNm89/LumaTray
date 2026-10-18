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
        private Thread _workerThread;
        private bool _running = true;

        private readonly List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>> _allocatedHandles =
            new List<KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>>();

        public event Action<MonitorInfo, int> BrightnessChanged;
        public event Action MonitorsRefreshed;

        public MonitorManager()
        {
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
                            // External physical monitor via DDC/CI
                            // Attempt SetVCPFeature (0x10 = Luminance) with retry, just like Test2
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
                            // Fallback to SetMonitorBrightness if SetVCPFeature wasn't accepted
                            if (!success)
                            {
                                NativeMethods.SetMonitorBrightness(mon.PhysicalHandle, (uint)val);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public void RefreshMonitors()
        {
            lock (_lock)
            {
                CleanupPhysicalHandles();
                _monitors.Clear();

                // 1. Discover DDC/CI Physical Monitors
                int displayIndex = 1;
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data)
                {
                    NativeMethods.MONITORINFOEX info = new NativeMethods.MONITORINFOEX();
                    info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                    NativeMethods.GetMonitorInfo(hMon, ref info);

                    string friendlyName = GetFriendlyNameFromDevice(info.szDevice);

                    uint count = 0;
                    if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) && count > 0)
                    {
                        var phys = new NativeMethods.PHYSICAL_MONITOR[count];
                        if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMon, count, phys))
                        {
                            _allocatedHandles.Add(new KeyValuePair<uint, NativeMethods.PHYSICAL_MONITOR[]>(count, phys));

                            for (int i = 0; i < count; i++)
                            {
                                uint min = 0, curr = 50, max = 100;
                                uint vcpType = 0, vcpCur = 0, vcpMax = 0;

                                // Prefer direct VCP 0x10 query, then fallback to GetMonitorBrightness
                                bool readOk = NativeMethods.GetVCPFeatureAndVCPFeatureReply(phys[i].hPhysicalMonitor, 0x10, out vcpType, out vcpCur, out vcpMax);
                                if (readOk && vcpMax > 0)
                                {
                                    min = 0;
                                    curr = vcpCur;
                                    max = vcpMax;
                                }
                                else
                                {
                                    readOk = NativeMethods.GetMonitorBrightness(phys[i].hPhysicalMonitor, out min, out curr, out max);
                                }

                                if (readOk)
                                {
                                    string name = friendlyName;
                                    if (string.IsNullOrEmpty(name) || name == "Generic PnP Monitor")
                                    {
                                        name = !string.IsNullOrEmpty(phys[i].szPhysicalMonitorDescription) && phys[i].szPhysicalMonitorDescription != "Generic PnP Monitor"
                                            ? phys[i].szPhysicalMonitorDescription
                                            : "Display " + displayIndex;
                                    }

                                    _monitors.Add(new MonitorInfo
                                    {
                                        Id = "DDC_" + displayIndex + "_" + i,
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
                    }
                    displayIndex++;
                    return true;
                }, IntPtr.Zero);

                // 2. Discover WMI Internal Displays (Laptops, Surface, etc.)
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness"))
                    {
                        foreach (ManagementObject mo in searcher.Get())
                        {
                            string instanceName = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : "Internal Display";
                            int curr = Convert.ToInt32(mo["CurrentBrightness"]);

                            string cleanName = "Internal Display";
                            if (instanceName.Contains(@"\"))
                            {
                                var parts = instanceName.Split('\\');
                                if (parts.Length > 1) cleanName = parts[1];
                            }

                            _monitors.Add(new MonitorInfo
                            {
                                Id = "WMI_" + instanceName,
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
                catch { }
            }

            if (MonitorsRefreshed != null)
            {
                MonitorsRefreshed();
            }
        }

        private void SetWmiBrightness(string instanceName, int brightness)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)brightness });
                        break;
                    }
                }
            }
            catch { }
        }

        private string GetFriendlyNameFromDevice(string deviceName)
        {
            NativeMethods.DISPLAY_DEVICE dev = new NativeMethods.DISPLAY_DEVICE();
            dev.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));

            if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref dev, 0))
            {
                string[] parts = dev.DeviceID.Split('\\');
                if (parts.Length >= 2)
                {
                    string hardwareId = parts[1];
                    try
                    {
                        using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + hardwareId))
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
                                                string edidName = ParseEdidName(edid);
                                                if (!string.IsNullOrEmpty(edidName))
                                                    return edidName;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(dev.DeviceString) && dev.DeviceString != "Generic PnP Monitor")
                    return dev.DeviceString;
            }
            return null;
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

        private void CleanupPhysicalHandles()
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

        public void Dispose()
        {
            _running = false;
            _workSignal.Set();
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(500);
            }
            CleanupPhysicalHandles();
            _workSignal.Close();
        }
    }
}
