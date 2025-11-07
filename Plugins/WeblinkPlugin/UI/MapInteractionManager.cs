using GMap.NET;
using GMap.NET.WindowsForms;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeblinkPlugin.Core.Http;
using WeblinkPlugin.Core.Http.Storage;

namespace WeblinkPlugin.UI
{
    public class MapInteractionManager : IDisposable
    {
        private readonly GMapControl _map;
        private readonly MarkerManager _markers;
        private readonly ServerManager _server;
        private readonly ContextMenuStrip _menu;

        private readonly System.Threading.Timer _sendTimer;
        private PointLatLng? _lastUserPoint;
        private bool _isSending;

        public MapInteractionManager(GMapControl map, MarkerManager markers, ServerManager server)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _server = server ?? throw new ArgumentNullException(nameof(server));

            _menu = new ContextMenuStrip();
            _menu.Items.Add("Відправити координати", null, OnAddMarkerClicked);
            _menu.Items.Add("Видалити всі мітки", null, OnClearMarkersClicked);
            _menu.Items.Add("Закрити", null, OnCloseClicked);

            _map.MouseClick += Map_MouseClick;

            _sendTimer = new System.Threading.Timer(async _ => await TryResendAsync(), null, 1000, 1000);
        }

        private void Map_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var point = _map.FromLocalToLatLng(e.X, e.Y);
                _menu.Tag = point;
                _menu.Show(_map, e.Location);
            }
        }

        private async void OnAddMarkerClicked(object sender, EventArgs e)
        {
            if (!(_menu.Tag is PointLatLng point))
                return;

            _markers.AddUserMarker(point.Lat, point.Lng, "User Marker");
            _lastUserPoint = point;

            await SendCoordinatesAsync(point);
        }

        private async Task SendCoordinatesAsync(PointLatLng point)
        {
            var state = SharedState.Instance;
            if (state == null) return;

            bool connected =
                state.ServerStatus?.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0 &&
                state.DeviceStatus?.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!connected)
            {
                Console.WriteLine("[MapInteractionManager] No connection, coordinates not sent");
                return;
            }

            var payload = new
            {
                type = "user_marker",
                lat = point.Lat,
                lon = point.Lng,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            try
            {
                bool ok = await _server.SendDataAsync(payload);
                Console.WriteLine(ok
                    ? $"[MapInteractionManager] Coordinates sent ({point.Lat:F6}, {point.Lng:F6})"
                    : "[MapInteractionManager] Send failed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapInteractionManager] Send error: {ex.Message}");
            }
        }

        private async Task TryResendAsync()
        {
            if (_isSending || !_lastUserPoint.HasValue)
                return;

            var state = SharedState.Instance;
            if (state == null)
                return;

            bool connected =
                state.ServerStatus?.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0 &&
                state.DeviceStatus?.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!connected)
                return;

            _isSending = true;
            try
            {
                await SendCoordinatesAsync(_lastUserPoint.Value);
            }
            finally
            {
                _isSending = false;
            }
        }

        private void OnClearMarkersClicked(object sender, EventArgs e)
        {
            _markers.ClearUserMarkers();
            _lastUserPoint = null;
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Owner is ContextMenuStrip menu)
                menu.Close();
        }

        public void Dispose()
        {
            _sendTimer?.Dispose();
        }
    }
}
