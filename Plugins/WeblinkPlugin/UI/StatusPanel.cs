using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeblinkPlugin.Core.Http.Storage;

namespace WeblinkPlugin.UI
{
    public class StatusPanel : UserControl
    {
        private readonly Label _statusLabel;

        public StatusPanel()
        {
            Dock = DockStyle.Fill;
            Height = 15;
            BackColor = Color.FromArgb(35, 35, 35);

            _statusLabel = new Label
            {
                Text = "Server: - | Device: - | Lat: - | Lon: - | Sats: - | IP: - | Port: -",
                ForeColor = Color.OrangeRed,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                AutoEllipsis = true
            };

            Controls.Add(_statusLabel);

            SharedState.Instance.StateChanged += OnStateChangedHandler;
        }

        private void OnStateChangedHandler()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)OnStateChangedHandler);
                return;
            }

            var s = SharedState.Instance;

            Color color;

            if (s.ServerStatus.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0)
                color = Color.OrangeRed;
            else if (s.DeviceStatus.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0)
                color = Color.LimeGreen;
            else
                color = Color.Khaki;

            _statusLabel.ForeColor = color;

            _statusLabel.Text =
                $"Server: {s.ServerStatus}  |  Device: {s.DeviceStatus}  |  " +
                $"Lat: {s.Lat:F6}  |  Lon: {s.Lon:F6}  |  Sats: {s.Satellites}  |  " +
                $"IP: {(string.IsNullOrEmpty(s.DeviceIp) ? "-" : s.DeviceIp)}  |  Port: {(s.DevicePort > 0 ? s.DevicePort.ToString() : "-")}";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SharedState.Instance.StateChanged -= OnStateChangedHandler;

            base.Dispose(disposing);
        }
    }
}
