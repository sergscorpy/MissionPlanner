using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    [Serializable]
    public class SituationMarkerMapMarker : GMapMarker
    {
        public string Label { get; set; }
        public bool IsHome { get; set; }
        public bool IsInterest { get; set; }

        public SituationMarkerMapMarker(PointLatLng pos)
            : base(pos)
        {
            Size = new Size(24, 24);
            Offset = new Point(-12, -12);
        }

        public override void OnRender(IGraphics g)
        {
            var center = new Point(LocalPosition.X - Offset.X, LocalPosition.Y - Offset.Y);
            var fill = IsHome ? Brushes.DeepSkyBlue : IsInterest ? Brushes.Gold : Brushes.LimeGreen;
            var outline = IsInterest ? new Pen(Color.OrangeRed, 3) : Pens.Black;

            g.FillEllipse(fill, center.X - 8, center.Y - 8, 16, 16);
            g.DrawEllipse(outline, center.X - 8, center.Y - 8, 16, 16);
            g.FillEllipse(Brushes.White, center.X - 3, center.Y - 3, 6, 6);

            if (string.IsNullOrEmpty(Label))
                return;

            using (var font = new Font(SystemFonts.DefaultFont.FontFamily, 8, FontStyle.Bold))
            {
                var size = g.MeasureString(Label, font);
                var rect = new RectangleF(center.X - size.Width / 2 - 4, center.Y - 30, size.Width + 8, size.Height + 4);
                g.FillRectangle(new SolidBrush(Color.FromArgb(220, Color.White)), rect);
                g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(Label, font, Brushes.Black, rect.X + 4, rect.Y + 2);
            }
        }
    }
}

