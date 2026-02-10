namespace WeblinkPlugin.Core.Http.Models
{
    public class OrchestratorStatus
    {
        public string Status { get; set; } = "disconnected";

        public string Ip { get; set; } = string.Empty;

        public int? Device_port { get; set; }

        public string Timestamp { get; set; } = string.Empty;
    }
}

