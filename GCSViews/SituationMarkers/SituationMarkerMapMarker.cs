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
        public bool DrawPin { get; set; } = true;

        public SituationMarkerMapMarker(PointLatLng pos)
            : base(pos)
        {
            Size = new Size(24, 24);
            Offset = new Point(-12, -12);
        }

        public override void OnRender(IGraphics g)
        {
            if (!DrawPin && string.IsNullOrEmpty(Label))
                return;

            var center = new Point(LocalPosition.X - Offset.X, LocalPosition.Y - Offset.Y);

            if (DrawPin)
            {
                var fill = IsHome ? Brushes.DeepSkyBlue : IsInterest ? Brushes.Gold : Brushes.LimeGreen;
                using (var outline = new Pen(IsInterest ? Color.OrangeRed : Color.Black, IsInterest ? 3 : 1))
                {
                    g.FillEllipse(fill, center.X - 8, center.Y - 8, 16, 16);
                    g.DrawEllipse(outline, center.X - 8, center.Y - 8, 16, 16);
                    g.FillEllipse(Brushes.White, center.X - 3, center.Y - 3, 6, 6);
                }
            }

            if (string.IsNullOrEmpty(Label))
                return;

            using (var font = new Font(SystemFonts.DefaultFont.FontFamily, 8, FontStyle.Bold))
            {
                var size = g.MeasureString(Label, font);
                var yOffset = DrawPin ? 30 : 24;
                var rect = new RectangleF(center.X - size.Width / 2 - 4, center.Y - yOffset, size.Width + 8, size.Height + 4);
                using (var background = new SolidBrush(Color.FromArgb(220, Color.White)))
                {
                    g.FillRectangle(background, rect);
                    g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
                    g.DrawString(Label, font, Brushes.Black, rect.X + 4, rect.Y + 2);
                }
            }
        }
    }
}

