namespace WeblinkPlugin.Core.Http.Models
{
    public class DeviceStatus
    {
        public bool Connected { get; set; }
        public int Sats { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Alt { get; set; }
        public double Hdop { get; set; }
        public string Timestamp { get; set; } = string.Empty;
    }
}

