using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class ElevationProfileControl : Control
    {
        readonly SituationMarkersManager manager;
        const double ProfilePaddingFraction = 0.12;
        const double DroneAltitudeScaleGuardFraction = 0.05;
        const double CursorSnapFraction = 0.005;
        const double DistanceKilometerMultiplier = 0.001;
        const string DistanceKilometerUnit = "Km";
        static readonly Color ProfileBackColor = Color.FromArgb(37, 37, 37);
        static readonly Color PlotBackColor = Color.FromArgb(45, 45, 45);
        static readonly Color AxisColor = Color.FromArgb(185, 185, 185);
        static readonly Color GridColor = Color.FromArgb(78, 78, 78);
        static readonly Color MutedTextColor = Color.FromArgb(170, 170, 170);
        static readonly Color TextColor = Color.FromArgb(230, 230, 230);
        static readonly Color TerrainColor = Color.FromArgb(72, 205, 92);
        static readonly Color TerrainFillColor = Color.FromArgb(70, 72, 205, 92);
        readonly List<ProfilePoint> profilePoints = new List<ProfilePoint>();
        readonly List<RouteMarkerDistance> markerDistances = new List<RouteMarkerDistance>();
        readonly List<double> profilePeakDistances = new List<double>();

        double minAltitude;
        double maxAltitude;
        double homeAltitude;
        double totalDistance;
        double minDistance;
        double maxDistance;
        double? droneDistance;
        double? droneAltitudeAmsl;
        double? cursorDistance;

        public ElevationProfileControl(SituationMarkersManager manager)
        {
            this.manager = manager;
            DoubleBuffered = true;
            BackColor = ProfileBackColor;
            ForeColor = TextColor;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var plot = GetPlotRectangle();
            if (!plot.Contains(e.Location) || profilePoints.Count < 2)
            {
                ClearCursorDistance();
                return;
            }

            cursorDistance = GetSnappedCursorDistance(ToDistance(plot, e.X));
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            ClearCursorDistance();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left || profilePoints.Count < 2)
                return;

            var plot = GetPlotRectangle();
            var marker = GetMarkerAtPoint(plot, e.Location);
            if (marker != null)
                manager.SelectMarker(marker);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button != MouseButtons.Left || profilePoints.Count < 2)
                return;

            var plot = GetPlotRectangle();
            if (!plot.Contains(e.Location))
                return;

            var marker = GetMarkerAtPoint(plot, e.Location);
            if (marker != null)
            {
                manager.SetInterestMarker(marker);
                return;
            }

            var distance = GetSnappedCursorDistance(ToDistance(plot, e.X));
            manager.InsertMarkerAtRouteDistance(distance);
        }

        public void RefreshProfile()
        {
            BuildProfile();
            if (profilePoints.Count < 2)
                cursorDistance = null;
            Invalidate();
        }

        public void RefreshDronePosition()
        {
            UpdateDroneProfilePosition();
            ExpandAltitudeRangeForDrone();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            try
            {
                DrawProfile(e.Graphics);
            }
            catch (Exception ex)
            {
                DrawRenderError(e.Graphics, ex);
            }
        }

        void DrawProfile(Graphics graphics)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var plot = GetPlotRectangle();
            UpdateDroneProfilePosition();
            ExpandAltitudeRangeForDrone();

            using (var axisPen = new Pen(AxisColor))
            using (var gridPen = new Pen(GridColor))
            using (var terrainPen = new Pen(TerrainColor, 2))
            using (var homePen = new Pen(Color.DeepSkyBlue, 1))
            using (var interestPen = new Pen(Color.OrangeRed, 1))
            using (var dronePen = new Pen(Color.Magenta, 2))
            {
                DrawPlotBackground(graphics, plot);
                DrawEmptyStateIfNeeded(graphics, plot);
                if (profilePoints.Count < 2)
                    return;

                var terrain = profilePoints.Select(p => ToPoint(plot, p.Distance, p.Altitude)).ToArray();
                DrawTerrainFill(graphics, plot, terrain);

                DrawRulers(graphics, plot, axisPen, gridPen);

                if (terrain.Length > 1)
                    graphics.DrawLines(terrainPen, terrain);

                DrawAltitudeLine(graphics, plot, manager.GetHomeMarker(), homePen, "HOME");
                DrawAltitudeLine(graphics, plot, manager.GetInterestMarker(), interestPen, "TARGET");

                DrawRouteMarkers(graphics, plot);
                DrawDroneProfile(graphics, plot, dronePen);

                DrawCursorProbe(graphics, plot);

            }
        }

        void DrawRenderError(Graphics g, Exception ex)
        {
            g.Clear(BackColor);
            using (var brush = new SolidBrush(Color.Firebrick))
            {
                var text = "Elevation profile render error: " + ex.Message;
                g.DrawString(text, Font, brush, 8, 8);
            }
        }

        void DrawPlotBackground(Graphics g, Rectangle plot)
        {
            using (var brush = new SolidBrush(PlotBackColor))
                g.FillRectangle(brush, plot);
        }

        void DrawTerrainFill(Graphics g, Rectangle plot, Point[] terrain)
        {
            if (terrain.Length < 2)
                return;

            var area = new List<Point>(terrain.Length + 2) { new Point(terrain[0].X, plot.Bottom) };
            area.AddRange(terrain);
            area.Add(new Point(terrain[terrain.Length - 1].X, plot.Bottom));

            using (var brush = new SolidBrush(TerrainFillColor))
                g.FillPolygon(brush, area.ToArray());
        }

        Rectangle GetPlotRectangle()
        {
            return new Rectangle(108, 24, Math.Max(10, Width - 158), Math.Max(10, Height - 108));
        }

        void ClearCursorDistance()
        {
            if (!cursorDistance.HasValue)
                return;

            cursorDistance = null;
            Invalidate();
        }

        void DrawEmptyStateIfNeeded(Graphics g, Rectangle plot)
        {
            if (profilePoints.Count >= 2)
                return;

            using (var brush = new SolidBrush(MutedTextColor))
            {
                var text = "Need at least two valid route points";
                var size = g.MeasureString(text, Font);
                g.DrawString(text, Font, brush, plot.Left + (plot.Width - size.Width) / 2, plot.Top + (plot.Height - size.Height) / 2);
            }
        }

        void BuildProfile()
        {
            profilePoints.Clear();
            markerDistances.Clear();
            profilePeakDistances.Clear();
            droneDistance = null;
            totalDistance = 0;
            minDistance = 0;
            maxDistance = 0;
            homeAltitude = 0;
            droneAltitudeAmsl = null;

            var route = manager.GetRouteMarkers();
            if (route.Count < 2)
                return;

            var home = manager.GetHomeMarker();
            if (home != null && home.Altitude.HasValue)
                homeAltitude = home.Altitude.Value;

            markerDistances.Add(new RouteMarkerDistance(route[0], 0));

            for (var i = 1; i < route.Count; i++)
            {
                var start = new PointLatLng(route[i - 1].Lat.Value, route[i - 1].Lng.Value);
                var end = new PointLatLng(route[i].Lat.Value, route[i].Lng.Value);
                var segmentLength = manager.DistanceMeters(start, end);
                var steps = Math.Max(2, Math.Min(80, (int)(segmentLength / 50.0)));

                for (var step = i == 1 ? 0 : 1; step <= steps; step++)
                {
                    var fraction = step / (double)steps;
                    var point = manager.Interpolate(start, end, fraction);
                    profilePoints.Add(new ProfilePoint(
                        totalDistance + segmentLength * fraction,
                        manager.GetTerrainAltitude(point.Lat, point.Lng)));
                }

                totalDistance += segmentLength;
                markerDistances.Add(new RouteMarkerDistance(route[i], totalDistance));
            }

            ExtendProfile(route);

            if (profilePoints.Count == 0)
                return;

            BuildProfilePeakSnapTargets();

            minAltitude = profilePoints.Min(a => ToRelativeAltitude(a.Altitude));
            maxAltitude = profilePoints.Max(a => ToRelativeAltitude(a.Altitude));

            AddReferenceAltitude(manager.GetHomeMarker());
            AddReferenceAltitude(manager.GetInterestMarker());
            UpdateDroneProfilePosition();
            if (droneAltitudeAmsl.HasValue)
                AddReferenceAltitude(droneAltitudeAmsl.Value);

            if (Math.Abs(maxAltitude - minAltitude) < 1)
            {
                maxAltitude += 1;
                minAltitude -= 1;
            }
            else
            {
                var altitudePadding = (maxAltitude - minAltitude) * ProfilePaddingFraction;
                maxAltitude += altitudePadding;
                minAltitude -= altitudePadding;
            }

        }

        void UpdateDroneProfilePosition()
        {
            droneDistance = null;
            droneAltitudeAmsl = null;

            if (manager.TryGetDroneAltitude(out var droneAltitude))
                droneAltitudeAmsl = droneAltitude;

            if (manager.TryGetDroneRouteDistance(out var distance))
                droneDistance = distance;
        }

        void ExpandAltitudeRangeForDrone()
        {
            if (!droneAltitudeAmsl.HasValue || profilePoints.Count < 2)
                return;

            var relativeAltitude = ToRelativeAltitude(droneAltitudeAmsl.Value);
            var range = maxAltitude - minAltitude;
            if (range <= 0)
                return;

            var lowerGuard = minAltitude + range * DroneAltitudeScaleGuardFraction;
            var upperGuard = maxAltitude - range * DroneAltitudeScaleGuardFraction;

            if (relativeAltitude < lowerGuard)
                minAltitude = maxAltitude - (maxAltitude - relativeAltitude) / (1.0 - DroneAltitudeScaleGuardFraction);
            else if (relativeAltitude > upperGuard)
                maxAltitude = minAltitude + (relativeAltitude - minAltitude) / (1.0 - DroneAltitudeScaleGuardFraction);
        }

        void ExtendProfile(List<SituationMarker> route)
        {
            if (totalDistance <= 0 || route.Count < 2)
                return;

            var extensionDistance = totalDistance * ProfilePaddingFraction;
            minDistance = -extensionDistance;
            maxDistance = totalDistance + extensionDistance;

            var first = new PointLatLng(route[0].Lat.Value, route[0].Lng.Value);
            var second = new PointLatLng(route[1].Lat.Value, route[1].Lng.Value);
            AddExtension(first, second, 0, -extensionDistance, true);

            var last = new PointLatLng(route[route.Count - 1].Lat.Value, route[route.Count - 1].Lng.Value);
            var previous = new PointLatLng(route[route.Count - 2].Lat.Value, route[route.Count - 2].Lng.Value);
            AddExtension(previous, last, totalDistance, extensionDistance, false);
        }

        void AddExtension(PointLatLng from, PointLatLng to, double routeEdgeDistance, double extensionDistance, bool prepend)
        {
            var steps = Math.Max(2, Math.Min(40, (int)(Math.Abs(extensionDistance) / 50.0)));
            var points = new List<ProfilePoint>();
            var direction = extensionDistance < 0 ? -1 : 1;

            for (var step = 1; step <= steps; step++)
            {
                var fraction = step / (double)steps;
                var point = manager.Interpolate(from, to, direction < 0 ? -fraction : 1 + fraction);
                points.Add(new ProfilePoint(
                    routeEdgeDistance + extensionDistance * fraction,
                    manager.GetTerrainAltitude(point.Lat, point.Lng)));
            }

            if (prepend)
            {
                points.Reverse();
                profilePoints.InsertRange(0, points);
            }
            else
            {
                profilePoints.AddRange(points);
            }
        }

        void BuildProfilePeakSnapTargets()
        {
            profilePeakDistances.Clear();
            if (profilePoints.Count < 3)
                return;

            for (var i = 1; i < profilePoints.Count - 1; i++)
            {
                var previous = ToRelativeAltitude(profilePoints[i - 1].Altitude);
                var current = ToRelativeAltitude(profilePoints[i].Altitude);
                var next = ToRelativeAltitude(profilePoints[i + 1].Altitude);
                var isPeak = current >= previous && current > next;
                var isValley = current <= previous && current < next;
                if (!isPeak && !isValley)
                    continue;

                profilePeakDistances.Add(profilePoints[i].Distance);
            }
        }

        void AddReferenceAltitude(SituationMarker marker)
        {
            if (marker == null || !marker.Altitude.HasValue)
                return;

            AddReferenceAltitude(marker.Altitude.Value);
        }

        void AddReferenceAltitude(double altitudeAmsl)
        {
            var relativeAltitude = ToRelativeAltitude(altitudeAmsl);
            minAltitude = Math.Min(minAltitude, relativeAltitude);
            maxAltitude = Math.Max(maxAltitude, relativeAltitude);
        }

        void DrawAltitudeLine(Graphics g, Rectangle plot, SituationMarker marker, Pen pen, string label)
        {
            if (marker == null || !marker.Altitude.HasValue)
                return;

            var y = ToY(plot, ToRelativeAltitude(marker.Altitude.Value));
            g.DrawLine(pen, plot.Left, y, plot.Right, y);
            using (var brush = new SolidBrush(pen.Color))
                g.DrawString(label, Font, brush, plot.Left + 3, y - 16);
        }

        void DrawRulers(Graphics g, Rectangle plot, Pen axisPen, Pen gridPen)
        {
            DrawDistanceRuler(g, plot, gridPen);
            DrawAltitudeRuler(g, plot, gridPen);
            g.DrawRectangle(axisPen, plot);
        }

        void DrawAltitudeRuler(Graphics g, Rectangle plot, Pen gridPen)
        {
            var multiplier = CurrentState.multiplieralt;
            if (multiplier <= 0)
                multiplier = 1;

            var labelAxisX = plot.Left - 80;
            using (var axisPen = new Pen(AxisColor))
                g.DrawLine(axisPen, labelAxisX, plot.Top, labelAxisX, plot.Bottom);

            var minDisplay = minAltitude * multiplier;
            var maxDisplay = maxAltitude * multiplier;
            var minLabelY = ToY(plot, minAltitude);
            var maxLabelY = ToY(plot, maxAltitude);
            foreach (var tick in BuildNiceTicks(minDisplay, maxDisplay, 7))
            {
                var y = ToY(plot, tick / multiplier);
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                using (var axisPen = new Pen(AxisColor))
                    g.DrawLine(axisPen, plot.Left - 5, y, plot.Left, y);
                if (Math.Abs(y - minLabelY) > Font.Height && Math.Abs(y - maxLabelY) > Font.Height)
                    DrawLabelAxisTick(g, labelAxisX, y, FormatSignedValue(tick), TextColor);
            }

            DrawAltitudeRulerMark(g, plot, labelAxisX, minAltitude, FormatSignedValue(minDisplay), MutedTextColor);
            DrawAltitudeRulerMark(g, plot, labelAxisX, maxAltitude, FormatSignedValue(maxDisplay), MutedTextColor);

            DrawPlotValueLabel(g, plot, 0, "0", Color.DeepSkyBlue);
            DrawAltitudeUnitLabel(g, labelAxisX, plot.Bottom + 8);

            var interest = manager.GetInterestMarker();
            if (interest != null && interest.Altitude.HasValue)
            {
                var relativeInterest = ToRelativeAltitude(interest.Altitude.Value);
                DrawPlotValueLabel(g, plot, relativeInterest, FormatSignedValue(relativeInterest * multiplier), Color.OrangeRed);
            }

            if (cursorDistance.HasValue)
            {
                var cursorAltitude = GetProfileAltitudeAtDistance(cursorDistance.Value);
                if (cursorAltitude.HasValue)
                {
                    var relativeCursorAltitude = ToRelativeAltitude(cursorAltitude.Value);
                    DrawPlotValueLabel(g, plot, relativeCursorAltitude,
                        FormatSignedValue(relativeCursorAltitude * multiplier), Color.DodgerBlue);
                }
            }

            if (droneAltitudeAmsl.HasValue)
                DrawPlotValueLabel(g, plot, ToRelativeAltitude(droneAltitudeAmsl.Value),
                    FormatSignedValue(ToRelativeAltitude(droneAltitudeAmsl.Value) * multiplier), Color.Magenta);
        }

        void DrawAltitudeRulerMark(Graphics g, Rectangle plot, int labelAxisX, double relativeAltitude, string label, Color color)
        {
            if (relativeAltitude < minAltitude || relativeAltitude > maxAltitude)
                return;

            var y = ToY(plot, relativeAltitude);
            using (var pen = new Pen(color, 1))
            {
                g.DrawLine(pen, plot.Left - 6, y, plot.Left, y);
                DrawLabelAxisTick(g, labelAxisX, y, label, color);
            }
        }

        void DrawLabelAxisTick(Graphics g, int labelAxisX, int y, string label, Color color)
        {
            using (var pen = new Pen(color, 1))
            using (var brush = new SolidBrush(color))
            {
                g.DrawLine(pen, labelAxisX, y, labelAxisX + 6, y);
                g.DrawString(label, Font, brush, labelAxisX + 9, y - Font.Height / 2);
            }
        }

        void DrawPlotValueLabel(Graphics g, Rectangle plot, double relativeAltitude, string label, Color color)
        {
            if (relativeAltitude < minAltitude || relativeAltitude > maxAltitude)
                return;

            var y = ToY(plot, relativeAltitude);
            using (var pen = new Pen(color, 1))
            using (var brush = new SolidBrush(color))
            {
                g.DrawLine(pen, plot.Left - 8, y, plot.Left, y);
                var size = g.MeasureString(label, Font);
                g.DrawString(label, Font, brush, plot.Left - size.Width - 10, y - Font.Height / 2);
            }
        }

        void DrawDistanceRuler(Graphics g, Rectangle plot, Pen gridPen)
        {
            var minDisplay = ToDistanceKilometers(minDistance);
            var maxDisplay = ToDistanceKilometers(maxDistance);
            var labelAxisY = plot.Bottom + 46;
            using (var axisPen = new Pen(AxisColor))
                g.DrawLine(axisPen, plot.Left, labelAxisY, plot.Right, labelAxisY);

            var minLabelX = ToX(plot, minDistance);
            var maxLabelX = ToX(plot, maxDistance);
            var minLabel = FormatDistanceKilometers(minDisplay);
            var maxLabel = FormatDistanceKilometers(maxDisplay);
            var minLabelWidth = g.MeasureString(minLabel, Font).Width;
            var maxLabelWidth = g.MeasureString(maxLabel, Font).Width;
            foreach (var tick in BuildNiceTicks(minDisplay, maxDisplay, 8))
            {
                var x = ToX(plot, tick / DistanceKilometerMultiplier);
                g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                using (var axisPen = new Pen(AxisColor))
                    g.DrawLine(axisPen, x, plot.Bottom, x, plot.Bottom + 5);
                var tickLabel = FormatDistanceKilometers(tick);
                var tickLabelWidth = g.MeasureString(tickLabel, Font).Width;
                if (Math.Abs(x - minLabelX) > (tickLabelWidth + minLabelWidth) / 2 + 6 &&
                    Math.Abs(x - maxLabelX) > (tickLabelWidth + maxLabelWidth) / 2 + 6)
                    DrawDistanceLabelAxisTick(g, labelAxisY, x, tickLabel, TextColor);
            }

            DrawDistanceLabelAxisTick(g, labelAxisY, minLabelX, minLabel, MutedTextColor);
            DrawDistanceLabelAxisTick(g, labelAxisY, maxLabelX, maxLabel, MutedTextColor);

            if (droneDistance.HasValue)
                DrawPlotDistanceValueLabel(g, plot, droneDistance.Value, Color.Magenta);

            if (cursorDistance.HasValue)
                DrawPlotDistanceValueLabel(g, plot, cursorDistance.Value, Color.DodgerBlue);

            DrawDistanceUnitLabel(g, plot.Left, labelAxisY + 8);
        }

        void DrawPlotDistanceValueLabel(Graphics g, Rectangle plot, double distance, Color color)
        {
            if (distance < minDistance || distance > maxDistance)
                return;

            var x = ToX(plot, distance);
            var label = FormatDistanceKilometers(ToDistanceKilometers(distance));
            using (var pen = new Pen(color, 1))
            using (var brush = new SolidBrush(color))
            {
                g.DrawLine(pen, x, plot.Bottom, x, plot.Bottom + 8);
                var size = g.MeasureString(label, Font);
                g.DrawString(label, Font, brush, x - size.Width / 2, plot.Bottom + 10);
            }
        }

        void DrawAltitudeUnitLabel(Graphics g, int labelAxisX, int y)
        {
            using (var brush = new SolidBrush(MutedTextColor))
            {
                var unit = string.IsNullOrEmpty(CurrentState.AltUnit) ? "m" : CurrentState.AltUnit;
                g.DrawString(unit, Font, brush, labelAxisX + 9, y);
            }
        }

        void DrawDistanceUnitLabel(Graphics g, int plotLeft, int y)
        {
            using (var brush = new SolidBrush(MutedTextColor))
            {
                var size = g.MeasureString(DistanceKilometerUnit, Font);
                g.DrawString(DistanceKilometerUnit, Font, brush, plotLeft - size.Width - 8, y);
            }
        }

        void DrawDistanceLabelAxisTick(Graphics g, int labelAxisY, int x, string label, Color color)
        {
            using (var pen = new Pen(color, 1))
            using (var brush = new SolidBrush(color))
            {
                g.DrawLine(pen, x, labelAxisY - 6, x, labelAxisY);
                var size = g.MeasureString(label, Font);
                g.DrawString(label, Font, brush, x - size.Width / 2, labelAxisY - Font.Height - 7);
            }
        }

        IEnumerable<double> BuildNiceTicks(double minValue, double maxValue, int targetCount)
        {
            if (targetCount < 2 || Math.Abs(maxValue - minValue) < 0.001)
                yield break;

            var step = NiceNumber((maxValue - minValue) / (targetCount - 1), true);
            if (step <= 0)
                yield break;

            var start = Math.Ceiling(minValue / step) * step;
            var end = Math.Floor(maxValue / step) * step;

            for (var value = start; value <= end + step * 0.5; value += step)
                yield return Math.Abs(value) < step * 0.001 ? 0 : value;
        }

        double NiceNumber(double value, bool round)
        {
            if (value <= 0)
                return 0;

            var exponent = Math.Floor(Math.Log10(value));
            var fraction = value / Math.Pow(10, exponent);
            double niceFraction;

            if (round)
            {
                if (fraction < 1.5)
                    niceFraction = 1;
                else if (fraction < 3)
                    niceFraction = 2;
                else if (fraction < 7)
                    niceFraction = 5;
                else
                    niceFraction = 10;
            }
            else
            {
                if (fraction <= 1)
                    niceFraction = 1;
                else if (fraction <= 2)
                    niceFraction = 2;
                else if (fraction <= 5)
                    niceFraction = 5;
                else
                    niceFraction = 10;
            }

            return niceFraction * Math.Pow(10, exponent);
        }

        string FormatSignedValue(double value)
        {
            return (value >= 0 ? "+" : "") + value.ToString("0", CultureInfo.InvariantCulture);
        }

        double ToDistanceKilometers(double distanceMeters)
        {
            return distanceMeters * DistanceKilometerMultiplier;
        }

        string FormatDistanceKilometers(double distanceKilometers)
        {
            return distanceKilometers.ToString("0.00", CultureInfo.InvariantCulture);
        }

        double GetSnappedCursorDistance(double rawDistance)
        {
            var snapRange = (maxDistance - minDistance) * CursorSnapFraction;
            if (snapRange <= 0)
                return rawDistance;

            if (TryFindNearestSnapDistance(rawDistance, markerDistances.Select(a => a.Distance), snapRange, out var markerDistance))
                return markerDistance;

            if (TryFindNearestSnapDistance(rawDistance, profilePeakDistances, snapRange, out var peakDistance))
                return peakDistance;

            return rawDistance;
        }

        bool TryFindNearestSnapDistance(double rawDistance, IEnumerable<double> candidates, double snapRange, out double snapDistance)
        {
            snapDistance = rawDistance;
            var bestDelta = snapRange;
            var found = false;

            foreach (var candidate in candidates)
            {
                var delta = Math.Abs(candidate - rawDistance);
                if (delta > bestDelta)
                    continue;

                bestDelta = delta;
                snapDistance = candidate;
                found = true;
            }

            return found;
        }

        void DrawRouteMarkers(Graphics g, Rectangle plot)
        {
            foreach (var marker in markerDistances)
            {
                var altitude = GetProfileAltitudeAtDistance(marker.Distance);
                if (!altitude.HasValue)
                    continue;

                var x = ToX(plot, marker.Distance);
                var y = ToY(plot, ToRelativeAltitude(altitude.Value));
                var isSelected = manager.SelectedMarker == marker.Marker;
                var fill = marker.Marker.IsHome ? Color.DeepSkyBlue : marker.Marker.IsInterest ? Color.Gold : Color.LimeGreen;
                var outline = marker.Marker.IsInterest ? Color.OrangeRed : Color.FromArgb(35, 35, 35);

                if (isSelected)
                {
                    using (var glow = new Pen(Color.FromArgb(245, 245, 245), 5))
                    using (var selectedOutline = new Pen(Color.DodgerBlue, 3))
                    {
                        g.DrawEllipse(glow, x - 8, y - 8, 16, 16);
                        g.DrawEllipse(selectedOutline, x - 8, y - 8, 16, 16);
                    }
                }

                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(outline, marker.Marker.IsInterest ? 3 : 2))
                {
                    var radius = isSelected ? 6 : 4;
                    g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                }

                using (var brush = new SolidBrush(TextColor))
                    g.DrawString(marker.Marker.Name, Font, brush, x + 6, y - 16);
            }
        }

        void DrawDroneProfile(Graphics g, Rectangle plot, Pen dronePen)
        {
            if (!droneDistance.HasValue && !droneAltitudeAmsl.HasValue)
                return;

            int? x = null;
            int? y = null;

            if (droneDistance.HasValue)
            {
                x = ToX(plot, droneDistance.Value);
                using (var verticalPen = new Pen(Color.FromArgb(210, dronePen.Color), dronePen.Width))
                {
                    verticalPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(verticalPen, x.Value, plot.Top, x.Value, plot.Bottom);
                }
            }

            if (droneAltitudeAmsl.HasValue)
            {
                var relativeAltitude = ToRelativeAltitude(droneAltitudeAmsl.Value);
                y = ToY(plot, relativeAltitude);
                using (var altitudePen = new Pen(Color.FromArgb(210, dronePen.Color), 1))
                {
                    altitudePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(altitudePen, plot.Left, y.Value, plot.Right, y.Value);
                }
            }

            if (x.HasValue && y.HasValue)
                DrawDroneIcon(g, x.Value, y.Value, dronePen.Color);
        }

        void DrawDroneIcon(Graphics g, int x, int y, Color color)
        {
            using (var armPen = new Pen(color, 2))
            using (var outlinePen = new Pen(Color.FromArgb(30, 30, 30), 2))
            using (var fill = new SolidBrush(Color.FromArgb(245, color)))
            using (var bodyFill = new SolidBrush(Color.FromArgb(245, 245, 245)))
            {
                g.DrawLine(outlinePen, x - 12, y, x + 12, y);
                g.DrawLine(outlinePen, x, y - 12, x, y + 12);
                g.DrawLine(armPen, x - 12, y, x + 12, y);
                g.DrawLine(armPen, x, y - 12, x, y + 12);

                g.FillEllipse(fill, x - 16, y - 4, 8, 8);
                g.FillEllipse(fill, x + 8, y - 4, 8, 8);
                g.FillEllipse(fill, x - 4, y - 16, 8, 8);
                g.FillEllipse(fill, x - 4, y + 8, 8, 8);

                g.DrawEllipse(outlinePen, x - 16, y - 4, 8, 8);
                g.DrawEllipse(outlinePen, x + 8, y - 4, 8, 8);
                g.DrawEllipse(outlinePen, x - 4, y - 16, 8, 8);
                g.DrawEllipse(outlinePen, x - 4, y + 8, 8, 8);

                g.FillEllipse(bodyFill, x - 5, y - 5, 10, 10);
                g.DrawEllipse(armPen, x - 5, y - 5, 10, 10);
            }
        }

        void DrawCursorProbe(Graphics g, Rectangle plot)
        {
            if (!cursorDistance.HasValue)
                return;

            var altitude = GetProfileAltitudeAtDistance(cursorDistance.Value);
            if (!altitude.HasValue)
                return;

            var x = ToX(plot, cursorDistance.Value);
            var relativeAltitude = ToRelativeAltitude(altitude.Value);
            var y = ToY(plot, relativeAltitude);
            using (var crosshairPen = new Pen(Color.FromArgb(150, Color.DodgerBlue), 1))
            using (var markerFill = new SolidBrush(Color.FromArgb(235, Color.White)))
            using (var markerOutline = new Pen(Color.DodgerBlue, 2))
            {
                crosshairPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawLine(crosshairPen, x, plot.Top, x, plot.Bottom);
                g.DrawLine(crosshairPen, plot.Left, y, plot.Right, y);

                g.FillEllipse(markerFill, x - 5, y - 5, 10, 10);
                g.DrawEllipse(markerOutline, x - 5, y - 5, 10, 10);
            }

            DrawCursorAltitudeLabel(g, plot, x, y, relativeAltitude);
        }

        void DrawCursorAltitudeLabel(Graphics g, Rectangle plot, int x, int y, double relativeAltitude)
        {
            var value = relativeAltitude * CurrentState.multiplieralt;
            var text = (value >= 0 ? "+" : "") + value.ToString("0", CultureInfo.InvariantCulture) + " " + CurrentState.AltUnit;
            using (var font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold))
            {
                var size = g.MeasureString(text, font);
                var width = (int)Math.Ceiling(size.Width) + 12;
                var height = (int)Math.Ceiling(size.Height) + 6;
                var labelX = Math.Min(plot.Right - width - 2, x + 8);
                var labelY = Math.Max(plot.Top + 2, y - height - 8);

                using (var fill = new SolidBrush(Color.FromArgb(230, Color.AliceBlue)))
                using (var outline = new Pen(Color.FromArgb(170, Color.MidnightBlue), 1))
                using (var brush = new SolidBrush(Color.Navy))
                {
                    g.FillRectangle(fill, labelX, labelY, width, height);
                    g.DrawRectangle(outline, labelX, labelY, width, height);
                    g.DrawString(text, font, brush, labelX + 6, labelY + 3);
                }
            }
        }

        SituationMarker GetMarkerAtPoint(Rectangle plot, Point point)
        {
            SituationMarker bestMarker = null;
            var bestDeltaX = double.MaxValue;
            var selectedDistance = GetSnappedCursorDistance(ToDistance(plot, point.X));
            var selectedX = ToX(plot, selectedDistance);

            foreach (var marker in markerDistances)
            {
                var x = ToX(plot, marker.Distance);
                var deltaX = Math.Abs(selectedX - x);
                if (deltaX > 10 || deltaX >= bestDeltaX)
                    continue;

                bestDeltaX = deltaX;
                bestMarker = marker.Marker;
            }

            return bestMarker;
        }

        double? GetProfileAltitudeAtDistance(double distance)
        {
            if (profilePoints.Count == 0)
                return null;

            for (var i = 1; i < profilePoints.Count; i++)
            {
                var previous = profilePoints[i - 1];
                var current = profilePoints[i];
                if (distance < previous.Distance || distance > current.Distance)
                    continue;

                var span = current.Distance - previous.Distance;
                if (Math.Abs(span) < 0.001)
                    return current.Altitude;

                var fraction = (distance - previous.Distance) / span;
                return previous.Altitude + (current.Altitude - previous.Altitude) * fraction;
            }

            return profilePoints.OrderBy(a => Math.Abs(a.Distance - distance)).First().Altitude;
        }

        Point ToPoint(Rectangle plot, double distance, double altitude)
        {
            return new Point(ToX(plot, distance), ToY(plot, ToRelativeAltitude(altitude)));
        }

        int ToX(Rectangle plot, double distance)
        {
            var distanceRange = maxDistance - minDistance;
            if (distanceRange <= 0)
                return plot.Left;

            return plot.Left + (int)(plot.Width * Math.Max(0, Math.Min(1, (distance - minDistance) / distanceRange)));
        }

        double ToDistance(Rectangle plot, int x)
        {
            var fraction = (x - plot.Left) / (double)plot.Width;
            fraction = Math.Max(0, Math.Min(1, fraction));
            return minDistance + (maxDistance - minDistance) * fraction;
        }

        int ToY(Rectangle plot, double altitude)
        {
            var fraction = (altitude - minAltitude) / (maxAltitude - minAltitude);
            return plot.Bottom - (int)(plot.Height * Math.Max(0, Math.Min(1, fraction)));
        }

        double ToRelativeAltitude(double altitude)
        {
            return altitude - homeAltitude;
        }

        class ProfilePoint
        {
            public readonly double Distance;
            public readonly double Altitude;

            public ProfilePoint(double distance, double altitude)
            {
                Distance = distance;
                Altitude = altitude;
            }
        }

        class RouteMarkerDistance
        {
            public readonly SituationMarker Marker;
            public readonly double Distance;

            public RouteMarkerDistance(SituationMarker marker, double distance)
            {
                Marker = marker;
                Distance = distance;
            }
        }
    }
}
