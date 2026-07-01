using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using RCListener.Logging;

namespace RCListener.Transport
{
    public class PortScanner
    {
        private readonly ILogger log;
        private readonly string lastPortCacheFile;

        public string LastKnownGoodPort { get; private set; }

        public PortScanner(ILogger log, string cacheFilePath)
        {
            this.log = log;
            lastPortCacheFile = cacheFilePath;
        }

        public void LoadLastKnownPort()
        {
            try
            {
                if (File.Exists(lastPortCacheFile))
                {
                    var port = File.ReadAllText(lastPortCacheFile).Trim();
                    if (!string.IsNullOrEmpty(port))
                    {
                        LastKnownGoodPort = port;
                        log.Log($"[CFG] Loaded last known port: {port}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Log($"[CFG] Failed to load last port cache: {ex.Message}");
            }
        }

        public void RecordSuccessfulPort(string port)
        {
            if (string.IsNullOrEmpty(port))
                return;

            LastKnownGoodPort = port;

            try
            {
                File.WriteAllText(lastPortCacheFile, port);
            }
            catch (Exception ex)
            {
                log.Log($"[CFG] Failed to persist last port '{port}': {ex.Message}");
            }
        }

        public List<string> GetCandidatePorts()
        {
            var ports = FilterPortsWithWmi(SerialPort.GetPortNames())
                .Distinct()
                .OrderBy(GetPortNumber)
                .ThenBy(p => p)
                .ToList();

            if (!string.IsNullOrEmpty(LastKnownGoodPort) && ports.Contains(LastKnownGoodPort))
            {
                ports.Remove(LastKnownGoodPort);
                ports.Insert(0, LastKnownGoodPort);
            }

            return ports;
        }

        private IEnumerable<string> FilterPortsWithWmi(IEnumerable<string> ports)
        {
            var candidates = new HashSet<string>(ports ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (candidates.Count == 0)
                return Array.Empty<string>();

            try
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (var obj in searcher.Get().OfType<ManagementObject>())
                    {
                        var name = obj["Name"]?.ToString() ?? string.Empty;
                        var pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                        var portName = ExtractComPortName(name);
                        var match = string.IsNullOrEmpty(portName)
                            ? null
                            : candidates.FirstOrDefault(port => string.Equals(port, portName, StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrEmpty(match))
                            continue;

                        if (pnpId.IndexOf("VID_0483", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            pnpId.IndexOf("PID_5740", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            allowed.Add(match);
                        }
                    }
                }

                if (allowed.Count > 0)
                    return allowed;

                log.Log("[WMI] No ports matched VID/PID filter; waiting for device change event");
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                log.Log($"[WMI] FilterPortsWithWmi error: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static string ExtractComPortName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return null;

            var match = Regex.Match(deviceName, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int GetPortNumber(string port)
        {
            if (string.IsNullOrEmpty(port) || !port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return int.MaxValue;

            int number;
            return int.TryParse(port.Substring(3), out number) ? number : int.MaxValue;
        }
    }
}
