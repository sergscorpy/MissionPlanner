using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace WeblinkPlugin.UI
{
    public class MarkerManager : IDisposable
    {
        private readonly GMapControl _map;
        private readonly GMapOverlay _userOverlay;
        private readonly GMapOverlay _liveOverlay;
        private readonly GMapOverlay _starlinkOverlay;

        private readonly List<GMarkerGoogle> _userMarkers = new List<GMarkerGoogle>();
        private GMarkerGoogle _liveMarker;
        private GMarkerGoogle _starlinkMarker;

        private bool _disposed;
        private readonly object _lock = new object();

        public MarkerManager(GMapControl map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));

            _userOverlay = new GMapOverlay("user_markers");
            _liveOverlay = new GMapOverlay("live_marker");
            _starlinkOverlay = new GMapOverlay("starlink_marker");

            _map.Overlays.Add(_userOverlay);
            _map.Overlays.Add(_liveOverlay);
            _map.Overlays.Add(_starlinkOverlay);
        }

        public void AddUserMarker(double lat, double lng, string label = null)
        {
            _map.InvokeIfRequired(() =>
            {
                var marker = new GMarkerGoogle(new PointLatLng(lat, lng), GMarkerGoogleType.red_dot)
                {
                    ToolTipText = label ?? $"Lat: {lat:F6}\nLng: {lng:F6}",
                    ToolTipMode = MarkerTooltipMode.OnMouseOver
                };

                _userOverlay.Markers.Add(marker);
                _userMarkers.Add(marker);
                _map.Refresh();
            });
        }

        public void UpdateLiveMarker(double lat, double lng)
        {
            _map.InvokeIfRequired(() =>
            {
                _liveOverlay.Markers.Clear();

                _liveMarker = new GMarkerGoogle(new PointLatLng(lat, lng), GMarkerGoogleType.blue_dot)
                {
                    ToolTipText = $"Drone: {lat:F6}, {lng:F6}",
                    ToolTipMode = MarkerTooltipMode.Always
                };

                _liveOverlay.Markers.Add(_liveMarker);
                _map.Refresh();
            });
        }

        public void UpdateStarlinkMarker(double lat, double lng)
        {
            _map.InvokeIfRequired(() =>
            {
                _starlinkOverlay.Markers.Clear();

                _starlinkMarker = new GMarkerGoogle(new PointLatLng(lat, lng), GMarkerGoogleType.green_dot)
                {
                    ToolTipText = $"Starlink: {lat:F6}, {lng:F6}",
                    ToolTipMode = MarkerTooltipMode.Always
                };

                _starlinkOverlay.Markers.Add(_starlinkMarker);
                _map.Refresh();
            });
        }

        public void ClearUserMarkers()
        {
            _map.InvokeIfRequired(() =>
            {
                _userOverlay.Markers.Clear();
                _userMarkers.Clear();
                _map.Refresh();
            });
        }

        public void ClearLiveMarker()
        {
            _map.InvokeIfRequired(() =>
            {
                _liveOverlay.Markers.Clear();
                _liveMarker = null;
                _map.Refresh();
            });
        }

        public void ClearStarlinkMarker()
        {
            _map.InvokeIfRequired(() =>
            {
                _starlinkOverlay.Markers.Clear();
                _starlinkMarker = null;
                _map.Refresh();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _map.InvokeIfRequired(() =>
                {
                    _map.Overlays.Remove(_userOverlay);
                    _map.Overlays.Remove(_liveOverlay);
                    _map.Overlays.Remove(_starlinkOverlay);

                    _userOverlay.Clear();
                    _liveOverlay.Clear();
                    _starlinkOverlay.Clear();
                    _map.Refresh();
                });

                _userMarkers.Clear();
                _liveMarker = null;
                _starlinkMarker = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarkerManager] Dispose error: {ex.Message}");
            }
        }
    }

    public static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.InvokeRequired)
                control.BeginInvoke(action);
            else
                action();
        }
    }
}
