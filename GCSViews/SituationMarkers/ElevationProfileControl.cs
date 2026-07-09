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
        readonly List<ProfilePoint> profilePoints = new List<ProfilePoint>();
        readonly List<RouteMarkerDistance> markerDistances = new List<RouteMarkerDistance>();

        double minAltitude;
        double maxAltitude;
        double totalDistance;
        double? droneDistance;

        public ElevationProfileControl(SituationMarkersManager manager)
        {
            this.manager = manager;
            DoubleBuffered = true;
            BackColor = Color.White;
            ForeColor = Color.Black;
        }

        public void RefreshProfile()
        {
            BuildProfile();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var plot = new Rectangle(58, 24, Math.Max(10, Width - 92), Math.Max(10, Height - 68));

            using (var axisPen = new Pen(Color.FromArgb(90, 90, 90)))
            using (var gridPen = new Pen(Color.FromArgb(225, 225, 225)))
            using (var terrainPen = new Pen(Color.ForestGreen, 2))
            using (var homePen = new Pen(Color.DeepSkyBlue, 1))
            using (var interestPen = new Pen(Color.OrangeRed, 1))
            using (var markerPen = new Pen(Color.FromArgb(120, 80, 80, 80), 1))
            using (var dronePen = new Pen(Color.Magenta, 2))
            {
                DrawEmptyStateIfNeeded(e.Graphics, plot);
                if (profilePoints.Count < 2)
                    return;

                for (var i = 0; i <= 4; i++)
                {
                    var y = plot.Top + plot.Height * i / 4;
                    e.Graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                }

                e.Graphics.DrawRectangle(axisPen, plot);

                var terrain = profilePoints.Select(p => ToPoint(plot, p.Distance, p.Altitude)).ToArray();
                if (terrain.Length > 1)
                    e.Graphics.DrawLines(terrainPen, terrain);

                DrawAltitudeLine(e.Graphics, plot, manager.GetHomeMarker(), homePen, "HOME");
                DrawAltitudeLine(e.Graphics, plot, manager.GetInterestMarker(), interestPen, "INTEREST");

                foreach (var marker in markerDistances)
                {
                    var x = ToX(plot, marker.Distance);
                    e.Graphics.DrawLine(markerPen, x, plot.Top, x, plot.Bottom);
                    e.Graphics.DrawString(marker.Name, Font, Brushes.Black, x + 3, plot.Top + 3);
                }

                if (droneDistance.HasValue)
                {
                    var x = ToX(plot, droneDistance.Value);
                    e.Graphics.DrawLine(dronePen, x, plot.Top, x, plot.Bottom);
                    e.Graphics.DrawString("DRONE", Font, Brushes.Magenta, x + 3, plot.Bottom - 18);
                }

                DrawAxisLabels(e.Graphics, plot);
            }
        }

        void DrawEmptyStateIfNeeded(Graphics g, Rectangle plot)
        {
            if (profilePoints.Count >= 2)
                return;

            using (var brush = new SolidBrush(Color.FromArgb(100, 100, 100)))
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
            droneDistance = null;
            totalDistance = 0;

            var route = manager.GetRouteMarkers();
            if (route.Count < 2)
                return;

            markerDistances.Add(new RouteMarkerDistance(route[0].Name, 0));

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
                markerDistances.Add(new RouteMarkerDistance(route[i].Name, totalDistance));
            }

            if (profilePoints.Count == 0)
                return;

            minAltitude = profilePoints.Min(a => a.Altitude);
            maxAltitude = profilePoints.Max(a => a.Altitude);

            AddReferenceAltitude(manager.GetHomeMarker());
            AddReferenceAltitude(manager.GetInterestMarker());

            if (Math.Abs(maxAltitude - minAltitude) < 1)
            {
                maxAltitude += 1;
                minAltitude -= 1;
            }

            if (manager.TryGetDroneRouteDistance(out var distance))
                droneDistance = distance;
        }

        void AddReferenceAltitude(SituationMarker marker)
        {
            if (marker == null || !marker.Altitude.HasValue)
                return;

            minAltitude = Math.Min(minAltitude, marker.Altitude.Value);
            maxAltitude = Math.Max(maxAltitude, marker.Altitude.Value);
        }

        void DrawAltitudeLine(Graphics g, Rectangle plot, SituationMarker marker, Pen pen, string label)
        {
            if (marker == null || !marker.Altitude.HasValue)
                return;

            var y = ToY(plot, marker.Altitude.Value);
            g.DrawLine(pen, plot.Left, y, plot.Right, y);
            g.DrawString(label, Font, new SolidBrush(pen.Color), plot.Left + 3, y - 16);
        }

        void DrawAxisLabels(Graphics g, Rectangle plot)
        {
            g.DrawString((minAltitude * CurrentState.multiplieralt).ToString("0", CultureInfo.InvariantCulture), Font, Brushes.Black, 4, plot.Bottom - 10);
            g.DrawString((maxAltitude * CurrentState.multiplieralt).ToString("0", CultureInfo.InvariantCulture), Font, Brushes.Black, 4, plot.Top - 4);
            g.DrawString("0 m", Font, Brushes.Black, plot.Left - 8, plot.Bottom + 6);
            g.DrawString((totalDistance * CurrentState.multiplierdist).ToString("0", CultureInfo.InvariantCulture) + " " + CurrentState.DistanceUnit, Font, Brushes.Black, plot.Right - 80, plot.Bottom + 6);
        }

        Point ToPoint(Rectangle plot, double distance, double altitude)
        {
            return new Point(ToX(plot, distance), ToY(plot, altitude));
        }

        int ToX(Rectangle plot, double distance)
        {
            if (totalDistance <= 0)
                return plot.Left;

            return plot.Left + (int)(plot.Width * Math.Max(0, Math.Min(1, distance / totalDistance)));
        }

        int ToY(Rectangle plot, double altitude)
        {
            var fraction = (altitude - minAltitude) / (maxAltitude - minAltitude);
            return plot.Bottom - (int)(plot.Height * Math.Max(0, Math.Min(1, fraction)));
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
            public readonly string Name;
            public readonly double Distance;

            public RouteMarkerDistance(string name, double distance)
            {
                Name = name;
                Distance = distance;
            }
        }
    }
}
