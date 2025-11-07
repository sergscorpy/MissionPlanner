using System;

namespace WeblinkPlugin.Core.Http.Models
{
    internal class TelemetryPacket
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        public double Alt { get; set; }

        public int Sats { get; set; }

        public double Hdop { get; set; }

        public string Timestamp { get; set; } = string.Empty;
    }
}
