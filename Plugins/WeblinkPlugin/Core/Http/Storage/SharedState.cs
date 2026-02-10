using System;

namespace WeblinkPlugin.Core.Http.Storage
{
    public class SharedState
    {
        private static readonly Lazy<SharedState> _instance = new Lazy<SharedState>(() => new SharedState());
        public static SharedState Instance => _instance.Value;

        public string ServerStatus { get; private set; } = "Disconnected";
        public string DeviceStatus { get; private set; } = "Disconnected";
        public string CurrentMode { get; private set; } = "starlink";
        public int? Channel { get; private set; }
        public int? ChannelPwm { get; private set; }
        public double Lat { get; private set; }
        public double Lon { get; private set; }
        public double Alt { get; private set; }
        public int Satellites { get; private set; }
        public int Ping { get; private set; }
        public double? TerrainAlt { get; private set; }
        public string DeviceIp { get; private set; } = string.Empty;
        public int DevicePort { get; private set; } = 0;

        public event Action StateChanged;

        private readonly object _lock = new object();

        private SharedState() { }

        public void UpdateStatus(string server, string device, string ip = "", int port = 0)
        {
            lock (_lock)
            {
                ServerStatus = server;
                DeviceStatus = device;
                if (!string.IsNullOrEmpty(ip))
                    DeviceIp = ip;
                if (port > 0)
                    DevicePort = port;
            }
            StateChanged?.Invoke();
        }

        public void UpdateTerrainAltitude(double? terrainAlt)
        {
            lock (_lock)
            {
                TerrainAlt = terrainAlt;
            }

            StateChanged?.Invoke();
        }

        public void UpdateTelemetry(double lat, double lon, double alt, int sats)
        {
            lock (_lock)
            {
                Lat = lat;
                Lon = lon;
                Alt = alt;
                Satellites = sats;
            }
            StateChanged?.Invoke();
        }

        public void UpdateChanel(int channel)
        {
            lock (_lock)
            {
                Channel = channel;
            }
            StateChanged?.Invoke();
        }
        public void UpdateChanelPwm(int pwm)
        {
            lock (_lock)
            {
                ChannelPwm = pwm;
            }
            StateChanged?.Invoke();
        }

        public void UpdateCurrentMode(string mode)
        {
            lock (_lock)
            {
                CurrentMode = mode;
            }
            StateChanged?.Invoke();
        }

        public void UpdatePing(int ping)
        {
            lock (_lock)
            {
                Ping = ping;
            }
            StateChanged?.Invoke();
        }
    }
}
