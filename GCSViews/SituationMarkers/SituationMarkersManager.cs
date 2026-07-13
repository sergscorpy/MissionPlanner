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
        const int DroneUiUpdateIntervalMs = 250;
        const double DroneTerrainCacheDistanceMeters = 10.0;

        readonly myGMAP map;
        readonly GMapOverlay markersOverlay;
        readonly GMapOverlay routeOverlay;
        readonly Dictionary<Guid, SituationMarkerMapMarker> mapMarkers = new Dictionary<Guid, SituationMarkerMapMarker>();
        readonly List<RouteSegment> routeSegments = new List<RouteSegment>();
        readonly List<SituationMarkerMapMarker> insertMarkers = new List<SituationMarkerMapMarker>();
        readonly string autosavePath;

        SituationMarkersForm form;
        ElevationProfileForm elevationProfileForm;
        SituationMarker pickingMarker;
        Point pickingMouseDownLocation;
        SituationMarker draggingMarker;
        SituationMarkerMapMarker droneLabelMarker;
        PointLatLng lastDronePosition;
        double lastDroneAltitudeAmsl;
        double lastDroneGroundSpeedMetersPerSecond;
        DateTime lastDroneUiUpdateUtc = DateTime.MinValue;
        PointLatLng cachedDroneTerrainPosition;
        double cachedDroneTerrainAltitude;
        Guid? selectedMarkerId;
        Guid? lastMarkerClickId;
        RouteSegment lastInsertClickSegment;
        DateTime lastInsertClickTimeUtc;
        Point lastInsertClickLocation;
        DateTime lastMarkerClickTimeUtc;
        Point lastMarkerClickLocation;
        bool hasDronePosition;
        bool hasDroneTerrainCache;
        bool pickingMouseDown;
        bool pickingDragged;
        bool suppressAutosave;
        bool markersLocked;

        public BindingList<SituationMarker> Markers { get; } = new BindingList<SituationMarker>();

        public bool MarkersLocked
        {
            get { return markersLocked; }
        }

        public bool MapOverlaysVisible
        {
            get { return markersOverlay.IsVisibile && routeOverlay.IsVisibile; }
        }

        public event EventHandler MarkersChanged;
        public event EventHandler SelectedMarkerChanged;
        public event EventHandler MarkersLockChanged;

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

        public void ToggleMarkersForm(IWin32Window owner)
        {
            if (form != null && !form.IsDisposed)
            {
                form.Close();
                return;
            }

            ShowMarkersForm(owner);
        }

        public void ToggleMapOverlaysVisible()
        {
            SetMapOverlaysVisible(!MapOverlaysVisible);
        }

        public void SetMapOverlaysVisible(bool visible)
        {
            markersOverlay.IsVisibile = visible;
            routeOverlay.IsVisibile = visible;
            map.Refresh();
        }

        public bool FocusRouteOnMap()
        {
            var route = GetRouteMarkers();
            if (route.Count == 0)
                return false;

            var minLat = route.Min(a => a.Lat.Value);
            var maxLat = route.Max(a => a.Lat.Value);
            var minLng = route.Min(a => a.Lng.Value);
            var maxLng = route.Max(a => a.Lng.Value);

            if (route.Count == 1)
            {
                map.Position = new PointLatLng(route[0].Lat.Value, route[0].Lng.Value);
                return true;
            }

            var latPadding = Math.Max(0.0001, (maxLat - minLat) * 0.12);
            var lngPadding = Math.Max(0.0001, (maxLng - minLng) * 0.12);
            var bounds = RectLatLng.FromLTRB(
                minLng - lngPadding,
                maxLat + latPadding,
                maxLng + lngPadding,
                minLat - latPadding);

            return map.SetZoomToFitRect(bounds);
        }

        public SituationMarker AddMarker()
        {
            var marker = new SituationMarker
            {
                Name = "Marker " + (Markers.Count(a => !a.IsHome) + 1).ToString(CultureInfo.InvariantCulture)
            };

            Markers.Add(marker);
            SetDefaultInterest();
            SelectMarker(marker);
            OnMarkersChanged(true);
            return marker;
        }

        public SituationMarker InsertMarkerAtRouteDistance(double distanceMeters)
        {
            if (routeSegments.Count == 0 || distanceMeters <= 0)
                return null;

            foreach (var segment in routeSegments)
            {
                if (segment.Length <= 0)
                    continue;

                if (distanceMeters < segment.StartDistance || distanceMeters > segment.EndDistance)
                    continue;

                var fraction = (distanceMeters - segment.StartDistance) / segment.Length;
                if (fraction <= 0 || fraction >= 1)
                    return null;

                var point = Interpolate(segment.Start, segment.End, fraction);
                var marker = new SituationMarker
                {
                    Name = "Marker " + (Markers.Count(a => !a.IsHome) + 1).ToString(CultureInfo.InvariantCulture),
                    Lat = point.Lat,
                    Lng = point.Lng,
                    Altitude = GetSrtmAltitude(point.Lat, point.Lng),
                    AltitudeSource = SituationMarkerAltitudeSource.Srtm
                };

                var insertIndex = Markers.IndexOf(segment.EndMarker);
                if (insertIndex < 0)
                    Markers.Add(marker);
                else
                    Markers.Insert(insertIndex, marker);

                SetDefaultInterest();
                SelectMarker(marker);
                RebuildMap();
                OnMarkersChanged(true);
                return marker;
            }

            return null;
        }

        public SituationMarker AddOrUpdateHome(PointLatLng homeLocation, double? homeAltitudeAmsl)
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
            home.Lat = homeLocation.Lat;
            home.Lng = homeLocation.Lng;
            if (homeAltitudeAmsl.HasValue && !double.IsNaN(homeAltitudeAmsl.Value) && !double.IsInfinity(homeAltitudeAmsl.Value))
            {
                home.Altitude = homeAltitudeAmsl.Value;
                home.AltitudeSource = SituationMarkerAltitudeSource.Home;
            }
            else
            {
                home.Altitude = GetSrtmAltitude(homeLocation.Lat, homeLocation.Lng);
                home.AltitudeSource = SituationMarkerAltitudeSource.Srtm;
            }

            RebuildMap();
            SelectMarker(home);
            OnMarkersChanged(true);
            return home;
        }

        public void RemoveMarker(SituationMarker marker)
        {
            if (marker == null)
                return;

            if (selectedMarkerId == marker.Id)
                ClearSelectedMarker();

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
            pickingMouseDown = false;
            pickingDragged = false;
            draggingMarker = null;
            selectedMarkerId = null;
            lastMarkerClickId = null;
            lastInsertClickSegment = null;
            var wasLocked = markersLocked;
            markersLocked = false;

            Markers.Clear();
            markersOverlay.Markers.Clear();
            routeOverlay.Routes.Clear();
            routeSegments.Clear();
            insertMarkers.Clear();
            mapMarkers.Clear();

            if (droneLabelMarker != null)
                markersOverlay.Markers.Add(droneLabelMarker);

            map.Refresh();
            if (wasLocked)
                MarkersLockChanged?.Invoke(this, EventArgs.Empty);
            SelectedMarkerChanged?.Invoke(this, EventArgs.Empty);
            OnMarkersChanged(true);
        }

        public SituationMarker SelectedMarker
        {
            get { return selectedMarkerId.HasValue ? Markers.FirstOrDefault(a => a.Id == selectedMarkerId.Value) : null; }
        }

        public void SelectMarker(SituationMarker marker)
        {
            if (marker == null || selectedMarkerId == marker.Id)
                return;

            if (selectedMarkerId.HasValue && mapMarkers.TryGetValue(selectedMarkerId.Value, out var previousMapMarker))
                previousMapMarker.IsSelected = false;

            selectedMarkerId = marker.Id;
            if (mapMarkers.TryGetValue(marker.Id, out var mapMarker))
                mapMarker.IsSelected = true;

            map.Invalidate();
            SelectedMarkerChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSelectedMarker()
        {
            if (!selectedMarkerId.HasValue)
                return;

            if (mapMarkers.TryGetValue(selectedMarkerId.Value, out var mapMarker))
                mapMarker.IsSelected = false;

            selectedMarkerId = null;
            map.Invalidate();
            SelectedMarkerChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool DeleteSelectedMarker()
        {
            var marker = SelectedMarker;
            if (marker == null)
                return false;

            RemoveMarker(marker);
            return true;
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
            lastInsertClickSegment = null;
            pickingMouseDown = false;
            pickingDragged = false;
            if (form != null && !form.IsDisposed)
                form.SetStatus("Click on the map to set marker coordinates.");
        }

        public void SetMarkersLocked(bool locked)
        {
            if (markersLocked == locked)
                return;

            markersLocked = locked;
            if (markersLocked)
            {
                draggingMarker = null;
                ClearMarkerHover();
            }

            if (form != null && !form.IsDisposed)
                form.SetStatus(markersLocked ? "Marker dragging is locked." : "");

            MarkersLockChanged?.Invoke(this, EventArgs.Empty);
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
                lastInsertClickSegment = null;
                pickingMouseDown = true;
                pickingDragged = false;
                pickingMouseDownLocation = e.Location;
                return true;
            }

            var insertMarker = currentMarker as SituationMarkerMapMarker;
            if (insertMarker != null && insertMarker.IsInsertPoint && insertMarker.InsertSegment != null)
            {
                if (IsDoubleClickOnInsertMarker(insertMarker.InsertSegment, e))
                {
                    lastInsertClickSegment = null;
                    draggingMarker = null;
                    InsertMarkerAtRouteDistance(insertMarker.InsertSegment.StartDistance + insertMarker.InsertSegment.Length / 2.0);
                    return true;
                }

                RememberInsertMarkerClick(insertMarker.InsertSegment, e);
                return true;
            }

            if (currentMarker is SituationMarkerMapMarker situationMapMarker &&
                situationMapMarker.Tag is SituationMarker marker)
            {
                SelectMarker(marker);

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

            ClearSelectedMarker();
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

        bool IsDoubleClickOnInsertMarker(RouteSegment segment, MouseEventArgs e)
        {
            if (e.Clicks > 1)
                return true;

            if (lastInsertClickSegment != segment)
                return false;

            var elapsed = DateTime.UtcNow - lastInsertClickTimeUtc;
            if (elapsed.TotalMilliseconds > SystemInformation.DoubleClickTime)
                return false;

            var maxDistance = SystemInformation.DoubleClickSize;
            return Math.Abs(e.X - lastInsertClickLocation.X) <= maxDistance.Width &&
                   Math.Abs(e.Y - lastInsertClickLocation.Y) <= maxDistance.Height;
        }

        void RememberInsertMarkerClick(RouteSegment segment, MouseEventArgs e)
        {
            lastInsertClickSegment = segment;
            lastInsertClickTimeUtc = DateTime.UtcNow;
            lastInsertClickLocation = e.Location;
        }

        void SetMarkerHover(GMapMarker marker, bool isHovered)
        {
            if (!(marker is SituationMarkerMapMarker situationMapMarker))
                return;

            if (!situationMapMarker.IsInsertPoint && !(situationMapMarker.Tag is SituationMarker))
                return;

            var nextState = situationMapMarker.IsInsertPoint ? isHovered : isHovered && !MarkersLocked;
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
            if (pickingMarker != null)
            {
                if (pickingMouseDown && e.Button == MouseButtons.Left &&
                    IsMouseDrag(pickingMouseDownLocation, e.Location))
                {
                    pickingDragged = true;
                }

                return false;
            }

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
            if (pickingMarker != null)
            {
                if (e.Button != MouseButtons.Left)
                    return false;

                if (pickingMouseDown && !pickingDragged &&
                    !IsMouseDrag(pickingMouseDownLocation, e.Location))
                {
                    var pickedPoint = map.FromLocalToLatLng(e.X, e.Y);
                    SetMarkerPosition(pickingMarker, pickedPoint.Lat, pickedPoint.Lng, true);
                    pickingMarker = null;
                    if (form != null && !form.IsDisposed)
                        form.SetStatus("");
                }

                pickingMouseDown = false;
                pickingDragged = false;
                return true;
            }

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

        bool IsMouseDrag(Point start, Point current)
        {
            var dragSize = SystemInformation.DragSize;
            return Math.Abs(current.X - start.X) > dragSize.Width / 2 ||
                   Math.Abs(current.Y - start.Y) > dragSize.Height / 2;
        }

        public void UpdateDronePosition(PointLatLng position, double altitudeAmsl, double groundSpeedMetersPerSecond)
        {
            if (position.Lat == 0 && position.Lng == 0)
                return;

            lastDronePosition = position;
            lastDroneAltitudeAmsl = altitudeAmsl;
            lastDroneGroundSpeedMetersPerSecond = Math.Max(0, groundSpeedMetersPerSecond);
            hasDronePosition = true;

            var now = DateTime.UtcNow;
            if (droneLabelMarker != null &&
                (now - lastDroneUiUpdateUtc).TotalMilliseconds < DroneUiUpdateIntervalMs)
            {
                return;
            }

            lastDroneUiUpdateUtc = now;

            if (droneLabelMarker == null)
            {
                droneLabelMarker = new SituationMarkerMapMarker(position)
                {
                    IsHitTestVisible = false,
                    DrawPin = false
                };
                markersOverlay.Markers.Add(droneLabelMarker);
            }

            UpdateDroneTerrainCache(position);
            droneLabelMarker.Position = position;
            droneLabelMarker.Label = BuildDroneLabel(altitudeAmsl, lastDroneGroundSpeedMetersPerSecond);
            map.UpdateMarkerLocalPosition(droneLabelMarker);
            elevationProfileForm?.RefreshDronePosition();
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

        public void ToggleElevationProfile(IWin32Window owner)
        {
            if (elevationProfileForm != null && !elevationProfileForm.IsDisposed)
            {
                elevationProfileForm.Close();
                return;
            }

            ShowElevationProfile(owner);
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

        public List<RouteSegment> GetRouteSegments()
        {
            return routeSegments.ToList();
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

            if (routeSegments.Count == 0)
                return false;

            var bestDistance = double.MaxValue;
            var bestRouteDistance = 0.0;

            foreach (var segment in routeSegments)
            {
                if (segment.Length <= 0)
                    continue;

                var projection = ProjectToSegment(segment.Start, segment.End, lastDronePosition);
                var distanceToSegment = DistanceMeters(lastDronePosition, projection.Point);
                if (distanceToSegment < bestDistance)
                {
                    bestDistance = distanceToSegment;
                    bestRouteDistance = segment.StartDistance + segment.Length * projection.Fraction;
                }
            }

            distanceMeters = bestRouteDistance;
            return true;
        }

        public bool TryGetDroneAltitude(out double altitudeAmsl)
        {
            altitudeAmsl = lastDroneAltitudeAmsl;
            return hasDronePosition;
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
                mapMarker.IsSelected = selectedMarkerId == marker.Id;
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
        }

        void RebuildRoute()
        {
            routeOverlay.Routes.Clear();
            ClearInsertMarkers();
            routeSegments.Clear();

            var route = GetRouteMarkers();
            if (route.Count < 2)
                return;

            var accumulated = 0.0;
            for (var i = 1; i < route.Count; i++)
            {
                var start = new PointLatLng(route[i - 1].Lat.Value, route[i - 1].Lng.Value);
                var end = new PointLatLng(route[i].Lat.Value, route[i].Lng.Value);
                var segmentLength = DistanceMeters(start, end);
                if (segmentLength > 0)
                {
                    routeSegments.Add(new RouteSegment
                    {
                        StartMarker = route[i - 1],
                        EndMarker = route[i],
                        Start = start,
                        End = end,
                        Length = segmentLength,
                        StartDistance = accumulated
                    });
                }

                accumulated += segmentLength;
            }

            var points = route.Select(a => new PointLatLng(a.Lat.Value, a.Lng.Value)).ToList();
            var mapRoute = new GMapRoute(points, "situation route")
            {
                Stroke = new Pen(Color.DeepSkyBlue, 2)
            };
            routeOverlay.Routes.Add(mapRoute);
            RebuildInsertMarkers();
        }

        void RebuildInsertMarkers()
        {
            foreach (var segment in routeSegments)
            {
                var center = Interpolate(segment.Start, segment.End, 0.5);
                var insertMarker = new SituationMarkerMapMarker(center)
                {
                    IsInsertPoint = true,
                    InsertSegment = segment
                };
                insertMarkers.Add(insertMarker);
                markersOverlay.Markers.Add(insertMarker);
            }
        }

        void ClearInsertMarkers()
        {
            foreach (var marker in insertMarkers)
                markersOverlay.Markers.Remove(marker);

            insertMarkers.Clear();
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

        string BuildDroneLabel(double altitudeAmsl, double groundSpeedMetersPerSecond)
        {
            var lines = new List<string>();
            var interest = GetInterestMarker();

            if (hasDroneTerrainCache)
                lines.Add("Alt: " + FormatRelativeAltitude(altitudeAmsl - cachedDroneTerrainAltitude));

            if (interest != null && interest.Altitude.HasValue)
                lines.Add("Target: " + FormatRelativeAltitude(altitudeAmsl - interest.Altitude.Value));

            if (interest != null && interest.HasValidPosition)
            {
                var target = new PointLatLng(interest.Lat.Value, interest.Lng.Value);
                var home = GetHomeMarker();
                var distanceMeters = DistanceMeters(lastDronePosition, target);
                var bearing = FormatBearing(BearingDegrees(lastDronePosition, target));

                if (home != null && home.HasValidPosition)
                {
                    var homePosition = new PointLatLng(home.Lat.Value, home.Lng.Value);
                    bearing += " (" + FormatBearing(BearingDegrees(lastDronePosition, homePosition)) + ")";
                }

                lines.Add("BRG|" + bearing);
                lines.Add("SPD|" + FormatDualSpeed(groundSpeedMetersPerSecond));
                lines.Add("DST|" + FormatDistance(distanceMeters));
                lines.Add("ETA|" + FormatEta(distanceMeters, groundSpeedMetersPerSecond));
            }

            return string.Join("\n", lines);
        }

        bool UpdateDroneTerrainCache(PointLatLng position)
        {
            if (hasDroneTerrainCache &&
                DistanceMeters(cachedDroneTerrainPosition, position) < DroneTerrainCacheDistanceMeters)
            {
                return false;
            }

            cachedDroneTerrainPosition = position;
            cachedDroneTerrainAltitude = GetSrtmAltitude(position.Lat, position.Lng);
            hasDroneTerrainCache = true;
            return true;
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
            RefreshDroneLabel();
            elevationProfileForm?.RefreshProfile();

            if (save)
                SaveAutosave();
        }

        void RefreshDroneLabel()
        {
            if (droneLabelMarker == null || !hasDronePosition)
                return;

            if (!hasDroneTerrainCache)
                UpdateDroneTerrainCache(lastDronePosition);

            droneLabelMarker.Label = BuildDroneLabel(lastDroneAltitudeAmsl, lastDroneGroundSpeedMetersPerSecond);
            map.UpdateMarkerLocalPosition(droneLabelMarker);
        }

        double BearingDegrees(PointLatLng from, PointLatLng to)
        {
            var lat1 = DegreesToRadians(from.Lat);
            var lat2 = DegreesToRadians(to.Lat);
            var dLng = DegreesToRadians(to.Lng - from.Lng);
            var y = Math.Sin(dLng) * Math.Cos(lat2);
            var x = Math.Cos(lat1) * Math.Sin(lat2) -
                    Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLng);
            return (RadiansToDegrees(Math.Atan2(y, x)) + 360.0) % 360.0;
        }

        static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        string FormatBearing(double bearing)
        {
            return bearing.ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        string FormatDualSpeed(double metersPerSecond)
        {
            var kilometersPerHour = metersPerSecond * 3.6;
            return metersPerSecond.ToString("0.0", CultureInfo.CurrentCulture) + " m/s | " +
                   kilometersPerHour.ToString("0", CultureInfo.InvariantCulture) + " km/h";
        }

        string FormatDistance(double meters)
        {
            if (meters >= 1000)
                return (meters / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " km";

            return meters.ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        string FormatEta(double distanceMeters, double speedMetersPerSecond)
        {
            if (speedMetersPerSecond < 0.5)
                return "--:--";

            var seconds = Math.Max(0, (int)Math.Round(distanceMeters / speedMetersPerSecond));
            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
                return ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" +
                       time.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                       time.Seconds.ToString("00", CultureInfo.InvariantCulture);

            return time.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   time.Seconds.ToString("00", CultureInfo.InvariantCulture);
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
                InterestMarkerId = Markers.FirstOrDefault(a => a.IsInterest)?.Id
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
                routeSegments.Clear();
                insertMarkers.Clear();
                mapMarkers.Clear();
                selectedMarkerId = null;
                var lockChanged = markersLocked;
                markersLocked = false;

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
                if (lockChanged)
                    MarkersLockChanged?.Invoke(this, EventArgs.Empty);
                SelectedMarkerChanged?.Invoke(this, EventArgs.Empty);
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

        public class RouteSegment
        {
            public SituationMarker StartMarker;
            public SituationMarker EndMarker;
            public PointLatLng Start;
            public PointLatLng End;
            public double Length;
            public double StartDistance;

            public double EndDistance
            {
                get { return StartDistance + Length; }
            }
        }
    }
}
