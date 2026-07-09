using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using Newtonsoft.Json;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class SituationMarkersManager : IDisposable
    {
        const string OverlayId = "situationmarkers";
        const string RouteOverlayId = "situationmarkersroute";
        const string AutosaveFileName = "autosavemarkers.json";

        readonly myGMAP map;
        readonly GMapOverlay markersOverlay;
        readonly GMapOverlay routeOverlay;
        readonly Dictionary<Guid, SituationMarkerMapMarker> mapMarkers = new Dictionary<Guid, SituationMarkerMapMarker>();
        readonly string autosavePath;

        SituationMarkersForm form;
        ElevationProfileForm elevationProfileForm;
        SituationMarker pickingMarker;
        SituationMarker draggingMarker;
        SituationMarkerMapMarker droneLabelMarker;
        PointLatLng lastDronePosition;
        Guid? lastMarkerClickId;
        DateTime lastMarkerClickTimeUtc;
        Point lastMarkerClickLocation;
        bool hasDronePosition;
        bool suppressAutosave;
        bool markersLocked;

        public BindingList<SituationMarker> Markers { get; } = new BindingList<SituationMarker>();

        public bool MarkersLocked
        {
            get { return markersLocked; }
        }

        public event EventHandler MarkersChanged;

        public SituationMarkersManager(myGMAP map)
        {
            this.map = map;
            autosavePath = Path.Combine(Settings.GetUserDataDirectory(), AutosaveFileName);

            markersOverlay = new GMapOverlay(OverlayId);
            routeOverlay = new GMapOverlay(RouteOverlayId);

            map.Overlays.Add(routeOverlay);
            map.Overlays.Add(markersOverlay);

            LoadAutosave();
        }

        public void ShowMarkersForm(IWin32Window owner)
        {
            if (form == null || form.IsDisposed)
            {
                form = new SituationMarkersForm(this);
                form.FormClosed += (sender, args) => form = null;
            }

            form.Show(owner);
            form.BringToFront();
        }

        public SituationMarker AddMarker()
        {
            var marker = new SituationMarker
            {
                Name = "Marker " + (Markers.Count(a => !a.IsHome) + 1).ToString(CultureInfo.InvariantCulture)
            };

            Markers.Add(marker);
            SetDefaultInterest();
            OnMarkersChanged(true);
            return marker;
        }

        public SituationMarker AddOrUpdateHome(PointLatLng homeLocation)
        {
            if (homeLocation.Lat == 0 && homeLocation.Lng == 0)
            {
                CustomMessageBox.Show("HOME location is not available yet.", "Situation markers");
                return null;
            }

            var home = Markers.FirstOrDefault(a => a.IsHome);
            if (home == null)
            {
                home = new SituationMarker
                {
                    Name = "HOME",
                    IsHome = true
                };
                Markers.Insert(0, home);
            }

            home.Name = "HOME";
            home.IsHome = true;
            SetMarkerPosition(home, homeLocation.Lat, homeLocation.Lng, true);
            OnMarkersChanged(true);
            return home;
        }

        public void RemoveMarker(SituationMarker marker)
        {
            if (marker == null)
                return;

            Markers.Remove(marker);
            if (mapMarkers.TryGetValue(marker.Id, out var mapMarker))
            {
                markersOverlay.Markers.Remove(mapMarker);
                mapMarkers.Remove(marker.Id);
            }

            SetDefaultInterest();
            RebuildMap();
            OnMarkersChanged(true);
        }

        public void ResetMarkers()
        {
            pickingMarker = null;
            draggingMarker = null;
            lastMarkerClickId = null;
            markersLocked = false;

            Markers.Clear();
            markersOverlay.Markers.Clear();
            routeOverlay.Routes.Clear();
            mapMarkers.Clear();

            if (droneLabelMarker != null)
                markersOverlay.Markers.Add(droneLabelMarker);

            map.Refresh();
            OnMarkersChanged(true);
        }

        public void SetInterestMarker(SituationMarker marker)
        {
            foreach (var item in Markers)
                item.IsInterest = item == marker;

            RebuildMap();
            OnMarkersChanged(true);
        }

        public void SetMarkerPosition(SituationMarker marker, double lat, double lng, bool updateAltitudeFromSrtm)
        {
            if (marker == null)
                return;

            marker.Lat = lat;
            marker.Lng = lng;

            if (updateAltitudeFromSrtm)
            {
                marker.Altitude = GetSrtmAltitude(lat, lng);
                marker.AltitudeSource = SituationMarkerAltitudeSource.Srtm;
            }

            if (!marker.IsHome && Markers.Any(a => a.HasValidPosition && a.IsInterest) == false)
                marker.IsInterest = true;

            RebuildMap();
            OnMarkersChanged(true);
        }

        public void UpdateMarkerCoordinatesFromTable(SituationMarker marker, double? lat, double? lng)
        {
            if (marker == null)
                return;

            marker.Lat = lat;
            marker.Lng = lng;

            if (marker.HasValidPosition)
            {
                marker.Altitude = GetSrtmAltitude(marker.Lat.Value, marker.Lng.Value);
                marker.AltitudeSource = SituationMarkerAltitudeSource.Srtm;
            }

            SetDefaultInterest();
            RebuildMap();
            OnMarkersChanged(true);
        }

        public void SetManualAltitude(SituationMarker marker, double altitude)
        {
            if (marker == null)
                return;

            marker.Altitude = altitude;
            marker.AltitudeSource = SituationMarkerAltitudeSource.Manual;
            RebuildMap();
            OnMarkersChanged(true);
        }

        public void NotifyMarkerEdited()
        {
            RebuildMap();
            OnMarkersChanged(true);
        }

        public void BeginPickOnMap(SituationMarker marker)
        {
            pickingMarker = marker;
            if (form != null && !form.IsDisposed)
                form.SetStatus("Click on the map to set marker coordinates.");
        }

        public void SetMarkersLocked(bool locked)
        {
            markersLocked = locked;
            if (markersLocked)
            {
                draggingMarker = null;
                ClearMarkerHover();
            }

            if (form != null && !form.IsDisposed)
                form.SetStatus(markersLocked ? "Marker dragging is locked." : "");

            OnMarkersChanged(true);
        }

        public bool HandleMouseDoubleClick(MouseEventArgs e, GMapMarker currentMarker)
        {
            if (e.Button != MouseButtons.Left)
                return false;

            if (currentMarker is SituationMarkerMapMarker situationMapMarker &&
                situationMapMarker.Tag is SituationMarker marker)
            {
                draggingMarker = null;
                SetInterestMarker(marker);
                return true;
            }

            return false;
        }

        public void HandleMarkerEnter(GMapMarker marker)
        {
            SetMarkerHover(marker, true);
        }

        public void HandleMarkerLeave(GMapMarker marker)
        {
            SetMarkerHover(marker, false);
        }

        public bool HandleMouseDown(MouseEventArgs e, GMapMarker currentMarker)
        {
            if (e.Button != MouseButtons.Left)
                return false;

            if (pickingMarker != null)
            {
                lastMarkerClickId = null;
                var point = map.FromLocalToLatLng(e.X, e.Y);
                SetMarkerPosition(pickingMarker, point.Lat, point.Lng, true);
                pickingMarker = null;
                if (form != null && !form.IsDisposed)
                    form.SetStatus("");
                return true;
            }

            if (currentMarker is SituationMarkerMapMarker situationMapMarker &&
                situationMapMarker.Tag is SituationMarker marker)
            {
                if (IsDoubleClickOnMarker(marker, e))
                {
                    lastMarkerClickId = null;
                    draggingMarker = null;
                    SetInterestMarker(marker);
                    return true;
                }

                RememberMarkerClick(marker, e);

                if (MarkersLocked)
                    return false;

                draggingMarker = marker;
                return true;
            }

            return false;
        }

        bool IsDoubleClickOnMarker(SituationMarker marker, MouseEventArgs e)
        {
            if (e.Clicks > 1)
                return true;

            if (lastMarkerClickId != marker.Id)
                return false;

            var elapsed = DateTime.UtcNow - lastMarkerClickTimeUtc;
            if (elapsed.TotalMilliseconds > SystemInformation.DoubleClickTime)
                return false;

            var maxDistance = SystemInformation.DoubleClickSize;
            return Math.Abs(e.X - lastMarkerClickLocation.X) <= maxDistance.Width &&
                   Math.Abs(e.Y - lastMarkerClickLocation.Y) <= maxDistance.Height;
        }

        void RememberMarkerClick(SituationMarker marker, MouseEventArgs e)
        {
            lastMarkerClickId = marker.Id;
            lastMarkerClickTimeUtc = DateTime.UtcNow;
            lastMarkerClickLocation = e.Location;
        }

        void SetMarkerHover(GMapMarker marker, bool isHovered)
        {
            if (!(marker is SituationMarkerMapMarker situationMapMarker) ||
                !(situationMapMarker.Tag is SituationMarker))
                return;

            var nextState = isHovered && !MarkersLocked;
            if (situationMapMarker.IsHovered == nextState)
                return;

            situationMapMarker.IsHovered = nextState;
            map.Invalidate();
        }

        void ClearMarkerHover()
        {
            var changed = false;
            foreach (var marker in mapMarkers.Values)
            {
                if (!marker.IsHovered)
                    continue;

                marker.IsHovered = false;
                changed = true;
            }

            if (changed)
                map.Invalidate();
        }

        public bool HandleMouseMove(MouseEventArgs e)
        {
            if (MarkersLocked)
                return false;

            if (draggingMarker == null || e.Button != MouseButtons.Left)
                return false;

            var point = map.FromLocalToLatLng(e.X, e.Y);
            if (mapMarkers.TryGetValue(draggingMarker.Id, out var mapMarker))
                mapMarker.Position = point;

            map.Invalidate();
            return true;
        }

        public bool HandleMouseUp(MouseEventArgs e)
        {
            if (MarkersLocked)
                return false;

            if (draggingMarker == null)
                return false;

            var point = map.FromLocalToLatLng(e.X, e.Y);
            var marker = draggingMarker;
            draggingMarker = null;
            SetMarkerPosition(marker, point.Lat, point.Lng, true);
            return true;
        }

        public void UpdateDronePosition(PointLatLng position, double altitudeAmsl)
        {
            if (position.Lat == 0 && position.Lng == 0)
                return;

            lastDronePosition = position;
            hasDronePosition = true;

            if (droneLabelMarker == null)
            {
                droneLabelMarker = new SituationMarkerMapMarker(position)
                {
                    IsHitTestVisible = false,
                    DrawPin = false
                };
                markersOverlay.Markers.Add(droneLabelMarker);
            }

            droneLabelMarker.Position = position;
            droneLabelMarker.Label = BuildDroneLabel(altitudeAmsl);
            map.UpdateMarkerLocalPosition(droneLabelMarker);
            elevationProfileForm?.RefreshProfile();
        }

        public void ShowElevationProfile(IWin32Window owner)
        {
            if (elevationProfileForm == null || elevationProfileForm.IsDisposed)
            {
                elevationProfileForm = new ElevationProfileForm(this);
                elevationProfileForm.FormClosed += (sender, args) => elevationProfileForm = null;
            }

            elevationProfileForm.Show(owner);
            elevationProfileForm.BringToFront();
            elevationProfileForm.RefreshProfile();
        }

        public List<SituationMarker> GetRouteMarkers()
        {
            var result = new List<SituationMarker>();
            var home = Markers.FirstOrDefault(a => a.IsHome && a.HasValidPosition);
            if (home != null)
                result.Add(home);

            result.AddRange(Markers.Where(a => !a.IsHome && a.HasValidPosition));
            return result;
        }

        public SituationMarker GetHomeMarker()
        {
            return Markers.FirstOrDefault(a => a.IsHome && a.HasValidPosition);
        }

        public SituationMarker GetInterestMarker()
        {
            return Markers.FirstOrDefault(a => a.IsInterest && a.HasValidPosition);
        }

        public double DistanceMeters(PointLatLng a, PointLatLng b)
        {
            if (map.MapProvider != null)
                return map.MapProvider.Projection.GetDistance(a, b) * 1000.0;

            var r = 6371000.0;
            var dLat = DegreesToRadians(b.Lat - a.Lat);
            var dLng = DegreesToRadians(b.Lng - a.Lng);
            var lat1 = DegreesToRadians(a.Lat);
            var lat2 = DegreesToRadians(b.Lat);
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        public double GetTerrainAltitude(double lat, double lng)
        {
            return GetSrtmAltitude(lat, lng);
        }

        public double? GetRelativeToHome(SituationMarker marker)
        {
            var home = GetHomeMarker();
            if (home == null || marker == null || !home.Altitude.HasValue || !marker.Altitude.HasValue)
                return null;

            return marker.Altitude.Value - home.Altitude.Value;
        }

        public bool TryGetDroneRouteDistance(out double distanceMeters)
        {
            distanceMeters = 0;
            if (!hasDronePosition)
                return false;

            var route = GetRouteMarkers();
            if (route.Count < 2)
                return false;

            var bestDistance = double.MaxValue;
            var accumulated = 0.0;
            var bestRouteDistance = 0.0;

            for (var i = 1; i < route.Count; i++)
            {
                var start = new PointLatLng(route[i - 1].Lat.Value, route[i - 1].Lng.Value);
                var end = new PointLatLng(route[i].Lat.Value, route[i].Lng.Value);
                var segmentLength = DistanceMeters(start, end);
                if (segmentLength <= 0)
                    continue;

                var projection = ProjectToSegment(start, end, lastDronePosition);
                var distanceToSegment = DistanceMeters(lastDronePosition, projection.Point);
                if (distanceToSegment < bestDistance)
                {
                    bestDistance = distanceToSegment;
                    bestRouteDistance = accumulated + segmentLength * projection.Fraction;
                }

                accumulated += segmentLength;
            }

            distanceMeters = bestRouteDistance;
            return true;
        }

        public PointLatLng Interpolate(PointLatLng start, PointLatLng end, double fraction)
        {
            return new PointLatLng(
                start.Lat + (end.Lat - start.Lat) * fraction,
                start.Lng + (end.Lng - start.Lng) * fraction);
        }

        public void SaveToFile(string fileName)
        {
            SaveToFile(fileName, false);
        }

        public void LoadFromFile(string fileName)
        {
            if (!File.Exists(fileName))
                return;

            var store = JsonConvert.DeserializeObject<SituationMarkersStore>(File.ReadAllText(fileName));
            LoadStore(store);
            OnMarkersChanged(false);
            SaveAutosave();
        }

        public void Dispose()
        {
            if (form != null && !form.IsDisposed)
                form.Close();

            if (elevationProfileForm != null && !elevationProfileForm.IsDisposed)
                elevationProfileForm.Close();

            map.Overlays.Remove(markersOverlay);
            map.Overlays.Remove(routeOverlay);
        }

        void RebuildMap()
        {
            foreach (var marker in Markers)
            {
                if (!marker.HasValidPosition)
                {
                    RemoveMapMarker(marker);
                    continue;
                }

                if (!mapMarkers.TryGetValue(marker.Id, out var mapMarker))
                {
                    mapMarker = new SituationMarkerMapMarker(new PointLatLng(marker.Lat.Value, marker.Lng.Value))
                    {
                        Tag = marker
                    };
                    mapMarkers[marker.Id] = mapMarker;
                    markersOverlay.Markers.Add(mapMarker);
                }

                mapMarker.Position = new PointLatLng(marker.Lat.Value, marker.Lng.Value);
                mapMarker.IsHome = marker.IsHome;
                mapMarker.IsInterest = marker.IsInterest;
                mapMarker.Label = BuildMarkerLabel(marker);
            }

            foreach (var key in mapMarkers.Keys.ToList())
            {
                if (Markers.All(a => a.Id != key))
                {
                    markersOverlay.Markers.Remove(mapMarkers[key]);
                    mapMarkers.Remove(key);
                }
            }

            RebuildRoute();
            map.Refresh();
            elevationProfileForm?.RefreshProfile();
        }

        void RebuildRoute()
        {
            routeOverlay.Routes.Clear();
            var points = GetRouteMarkers().Select(a => new PointLatLng(a.Lat.Value, a.Lng.Value)).ToList();
            if (points.Count < 2)
                return;

            var route = new GMapRoute(points, "situation route")
            {
                Stroke = new Pen(Color.DeepSkyBlue, 2)
            };
            routeOverlay.Routes.Add(route);
        }

        void RemoveMapMarker(SituationMarker marker)
        {
            if (!mapMarkers.TryGetValue(marker.Id, out var mapMarker))
                return;

            markersOverlay.Markers.Remove(mapMarker);
            mapMarkers.Remove(marker.Id);
        }

        string BuildMarkerLabel(SituationMarker marker)
        {
            if (marker.IsHome)
                return "HOME";

            var home = GetHomeMarker();
            if (home == null || !home.Altitude.HasValue || !marker.Altitude.HasValue)
                return marker.Name;

            return FormatRelativeAltitude(marker.Altitude.Value - home.Altitude.Value);
        }

        string BuildDroneLabel(double altitudeAmsl)
        {
            var lines = new List<string>();
            var home = GetHomeMarker();
            var interest = GetInterestMarker();

            if (home != null && home.Altitude.HasValue)
                lines.Add("Home: " + FormatRelativeAltitude(altitudeAmsl - home.Altitude.Value));

            if (interest != null && interest.Altitude.HasValue)
                lines.Add("Target: " + FormatRelativeAltitude(altitudeAmsl - interest.Altitude.Value));

            return string.Join("\n", lines);
        }

        string FormatRelativeAltitude(double altitude)
        {
            var value = altitude * CurrentState.multiplieralt;
            return (value >= 0 ? "+" : "") + value.ToString("0", CultureInfo.InvariantCulture) + " " + CurrentState.AltUnit;
        }

        void SetDefaultInterest()
        {
            if (Markers.Any(a => a.IsInterest && a.HasValidPosition))
                return;

            foreach (var marker in Markers)
                marker.IsInterest = false;

            var last = Markers.LastOrDefault(a => !a.IsHome && a.HasValidPosition);
            if (last != null)
                last.IsInterest = true;
        }

        double GetSrtmAltitude(double lat, double lng)
        {
            try
            {
                return srtm.getAltitude(lat, lng).alt;
            }
            catch
            {
                return 0;
            }
        }

        void OnMarkersChanged(bool save)
        {
            form?.RefreshGrid();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            elevationProfileForm?.RefreshProfile();

            if (save)
                SaveAutosave();
        }

        ProjectionResult ProjectToSegment(PointLatLng start, PointLatLng end, PointLatLng point)
        {
            var ax = start.Lng;
            var ay = start.Lat;
            var bx = end.Lng;
            var by = end.Lat;
            var px = point.Lng;
            var py = point.Lat;
            var dx = bx - ax;
            var dy = by - ay;
            var length2 = dx * dx + dy * dy;
            var fraction = length2 <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / length2;
            fraction = Math.Max(0, Math.Min(1, fraction));

            return new ProjectionResult
            {
                Fraction = fraction,
                Point = new PointLatLng(ay + dy * fraction, ax + dx * fraction)
            };
        }

        void SaveAutosave()
        {
            if (suppressAutosave)
                return;

            try
            {
                SaveToFile(autosavePath, true);
            }
            catch
            {
            }
        }

        void LoadAutosave()
        {
            if (!File.Exists(autosavePath))
                return;

            try
            {
                LoadFromFile(autosavePath);
            }
            catch
            {
            }
        }

        void SaveToFile(string fileName, bool isAutosave)
        {
            var store = new SituationMarkersStore
            {
                Markers = Markers.ToList(),
                InterestMarkerId = Markers.FirstOrDefault(a => a.IsInterest)?.Id,
                MarkersLocked = MarkersLocked
            };

            var directory = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fileName, JsonConvert.SerializeObject(store, Formatting.Indented));
        }

        void LoadStore(SituationMarkersStore store)
        {
            if (store == null || store.Markers == null)
                return;

            suppressAutosave = true;
            try
            {
                Markers.Clear();
                markersOverlay.Markers.Clear();
                routeOverlay.Routes.Clear();
                mapMarkers.Clear();
                markersLocked = store.MarkersLocked;

                foreach (var marker in store.Markers)
                    Markers.Add(marker);

                if (store.InterestMarkerId.HasValue)
                {
                    var interest = Markers.FirstOrDefault(a => a.Id == store.InterestMarkerId.Value);
                    foreach (var marker in Markers)
                        marker.IsInterest = marker == interest;
                }
                else
                {
                    SetDefaultInterest();
                }

                RebuildMap();
            }
            finally
            {
                suppressAutosave = false;
            }
        }

        static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        class ProjectionResult
        {
            public double Fraction;
            public PointLatLng Point;
        }
    }
}
