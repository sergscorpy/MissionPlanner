using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        public bool IsHovered { get; set; }
        public bool IsSelected { get; set; }
        public bool DrawPin { get; set; } = true;
        public bool IsInsertPoint { get; set; }
        public SituationMarkersManager.RouteSegment InsertSegment { get; set; }

        public SituationMarkerMapMarker(PointLatLng pos)
            : base(pos)
        {
            Size = new Size(24, 24);
            Offset = new Point(-12, -12);
        }

        public override void OnRender(IGraphics g)
        {
            if (IsInsertPoint)
            {
                DrawInsertPoint(g);
                return;
            }

            if (!DrawPin && string.IsNullOrEmpty(Label))
                return;

            var center = new Point(LocalPosition.X - Offset.X, LocalPosition.Y - Offset.Y);

            if (DrawPin)
            {
                var fill = IsHome ? Color.DeepSkyBlue : IsInterest ? Color.Gold : Color.LimeGreen;
                var isHighlighted = IsHovered || IsSelected;
                if (isHighlighted)
                    fill = IsHome ? Color.Aqua : IsInterest ? Color.Orange : Color.Chartreuse;

                if (isHighlighted)
                {
                    using (var glow = new Pen(Color.White, 5))
                    using (var hoverOutline = new Pen(Color.DodgerBlue, 3))
                    {
                        g.DrawEllipse(glow, center.X - 12, center.Y - 12, 24, 24);
                        g.DrawEllipse(hoverOutline, center.X - 12, center.Y - 12, 24, 24);
                    }
                }

                using (var brush = new SolidBrush(fill))
                using (var outline = new Pen(IsInterest ? Color.OrangeRed : Color.Black, IsInterest ? 3 : 1))
                {
                    g.FillEllipse(brush, center.X - 8, center.Y - 8, 16, 16);
                    g.DrawEllipse(outline, center.X - 8, center.Y - 8, 16, 16);
                    g.FillEllipse(Brushes.White, center.X - 3, center.Y - 3, 6, 6);
                }
            }

            if (string.IsNullOrEmpty(Label))
                return;

            if (DrawPin)
            {
                DrawMarkerLabel(g, center);
                return;
            }

            DrawDroneLabel(g, center);
        }

        void DrawInsertPoint(IGraphics g)
        {
            var center = new Point(LocalPosition.X - Offset.X, LocalPosition.Y - Offset.Y);

            if (!IsHovered)
            {
                using (var fill = new SolidBrush(Color.DeepSkyBlue))
                using (var outline = new Pen(Color.FromArgb(35, 35, 35), 1))
                {
                    g.FillEllipse(fill, center.X - 2, center.Y - 2, 4, 4);
                    g.DrawEllipse(outline, center.X - 2, center.Y - 2, 4, 4);
                }

                return;
            }

            using (var fill = new SolidBrush(Color.FromArgb(225, 35, 35, 35)))
            using (var outline = new Pen(Color.DeepSkyBlue, 2))
            using (var plus = new Pen(Color.White, 2))
            {
                g.FillEllipse(fill, center.X - 7, center.Y - 7, 14, 14);
                g.DrawEllipse(outline, center.X - 7, center.Y - 7, 14, 14);
                g.DrawLine(plus, center.X - 4, center.Y, center.X + 4, center.Y);
                g.DrawLine(plus, center.X, center.Y - 4, center.X, center.Y + 4);
            }
        }

        void DrawMarkerLabel(IGraphics g, Point center)
        {
            using (var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var labelText = Label;
                var direction = GetRelativeDirection(labelText);
                if (direction != 0 && (labelText.StartsWith("+") || labelText.StartsWith("-")))
                    labelText = labelText.Substring(1);

                var measuredTextSize = g.MeasureString(labelText, font);
                var textWidth = (int)Math.Ceiling(measuredTextSize.Width) + 4;
                var textHeight = (int)Math.Ceiling(measuredTextSize.Height);
                var iconWidth = direction == 0 ? 0 : 17;
                var contentGap = direction == 0 ? 0 : 4;
                var rect = new Rectangle(
                    center.X + 14,
                    center.Y - 44 - textHeight,
                    textWidth + iconWidth + contentGap + 10,
                    textHeight + 10);

                var theme = GetLabelTheme(direction);
                using (var stroke = new Pen(Color.FromArgb(180, theme.Stroke), 2))
                using (var fill = new SolidBrush(Color.FromArgb(226, theme.Fill)))
                using (var foreground = new SolidBrush(theme.Stroke))
                using (var format = new StringFormat())
                using (var bubblePath = CreateRoundedRectanglePath(rect, 7))
                {
                    stroke.LineJoin = LineJoin.Round;
                    stroke.StartCap = LineCap.RoundAnchor;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;

                    g.DrawLine(stroke, center.X, center.Y, rect.X, rect.Y + rect.Height / 2);
                    g.FillPath(fill, bubblePath);
                    g.DrawPath(stroke, bubblePath);

                    var textRect = new RectangleF(
                        rect.X + 5 + iconWidth + contentGap,
                        rect.Y,
                        rect.Width - 10 - iconWidth - contentGap,
                        rect.Height);

                    if (direction != 0)
                    {
                        var iconBounds = new RectangleF(rect.X + 5, rect.Y + (rect.Height - 17) / 2f, 17, 17);
                        DrawAltitudeDirectionIcon(g, iconBounds, direction, theme.Stroke);
                    }

                    g.DrawString(labelText, font, foreground, textRect, format);
                }
            }
        }

        int GetRelativeDirection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            if (text.StartsWith("+"))
                return 1;

            if (text.StartsWith("-"))
                return -1;

            return 0;
        }

        LabelTheme GetLabelTheme(int direction)
        {
            if (direction > 0)
                return new LabelTheme(Color.SeaGreen, Color.FromArgb(222, 246, 229));

            if (direction < 0)
                return new LabelTheme(Color.Firebrick, Color.FromArgb(252, 226, 226));

            return new LabelTheme(Color.MidnightBlue, Color.AliceBlue);
        }

        GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        void DrawAltitudeDirectionIcon(IGraphics g, RectangleF bounds, int direction, Color color)
        {
            var centerX = bounds.X + bounds.Width / 2f;
            var up = new PointF[]
            {
                new PointF(centerX, bounds.Y + 1),
                new PointF(bounds.X + bounds.Width - 2, bounds.Y + 7),
                new PointF(bounds.X + 2, bounds.Y + 7)
            };
            var down = new PointF[]
            {
                new PointF(centerX, bounds.Bottom - 1),
                new PointF(bounds.X + bounds.Width - 2, bounds.Bottom - 7),
                new PointF(bounds.X + 2, bounds.Bottom - 7)
            };

            using (var pen = new Pen(color, 1.4f))
            using (var brush = new SolidBrush(color))
            {
                if (direction > 0)
                {
                    g.FillPolygon(brush, up);
                    g.DrawPolygon(pen, up);
                    g.DrawPolygon(pen, down);
                    return;
                }

                g.DrawPolygon(pen, up);
                g.FillPolygon(brush, down);
                g.DrawPolygon(pen, down);
            }
        }

        struct LabelTheme
        {
            public readonly Color Stroke;
            public readonly Color Fill;

            public LabelTheme(Color stroke, Color fill)
            {
                Stroke = stroke;
                Fill = fill;
            }
        }

        void DrawDroneLabel(IGraphics g, Point center)
        {
            using (var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var lines = Label.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                    return;

                var paddingX = 7;
                var paddingY = 5;
                var lineHeight = (int)Math.Ceiling(g.MeasureString("Target: +000 m", font).Height);
                var lineSpacing = lines.Length > 1 ? 5 : 0;
                var iconWidth = 17;
                var contentGap = 4;
                var width = 0;

                foreach (var line in lines)
                    width = Math.Max(width, (int)Math.Ceiling(g.MeasureString(RemoveAltitudeSign(line), font).Width));

                width += paddingX * 2 + iconWidth + contentGap + 4;
                var height = lineHeight * lines.Length + lineSpacing * (lines.Length - 1) + paddingY * 2;
                var rect = new Rectangle(
                    center.X + 14,
                    center.Y - 44 - height,
                    width,
                    height);

                var theme = GetLabelTheme(0);
                using (var stroke = new Pen(Color.FromArgb(180, theme.Stroke), 2))
                using (var background = new SolidBrush(Color.FromArgb(226, theme.Fill)))
                using (var format = new StringFormat())
                using (var bubblePath = CreateRoundedRectanglePath(rect, 7))
                {
                    stroke.LineJoin = LineJoin.Round;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;

                    g.DrawLine(stroke, center.X, center.Y, rect.X, rect.Y + rect.Height / 2);
                    g.FillPath(background, bubblePath);
                    g.DrawPath(stroke, bubblePath);

                    if (lines.Length > 1)
                        DrawDroneLabelSeparators(g, rect, paddingX, paddingY, lineHeight, lineSpacing, lines.Length, theme.Stroke);

                    for (var i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var direction = GetRelativeDirection(ExtractAltitudeValue(line));
                        var color = GetLabelTheme(direction).Stroke;
                        var lineTop = rect.Y + paddingY + i * (lineHeight + lineSpacing);
                        var iconBounds = new RectangleF(
                            rect.X + paddingX,
                            lineTop + (lineHeight - 17) / 2f,
                            17,
                            17);
                        var textRect = new RectangleF(
                            rect.X + paddingX + iconWidth + contentGap,
                            lineTop,
                            rect.Width - paddingX * 2 - iconWidth - contentGap,
                            lineHeight);

                        if (direction != 0)
                            DrawAltitudeDirectionIcon(g, iconBounds, direction, color);

                        using (var foreground = new SolidBrush(color))
                            g.DrawString(RemoveAltitudeSign(line), font, foreground, textRect, format);
                    }
                }
            }
        }

        void DrawDroneLabelSeparators(IGraphics g, Rectangle rect, int paddingX, int paddingY, int lineHeight,
            int lineSpacing, int lineCount, Color color)
        {
            using (var pen = new Pen(Color.FromArgb(95, color), 1))
            {
                for (var i = 1; i < lineCount; i++)
                {
                    var y = rect.Y + paddingY + i * lineHeight + (i - 1) * lineSpacing + lineSpacing / 2f;
                    g.DrawLine(pen, rect.X + paddingX, y, rect.Right - paddingX, y);
                }
            }
        }

        string ExtractAltitudeValue(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var index = text.IndexOf(':');
            return index < 0 ? text.Trim() : text.Substring(index + 1).Trim();
        }

        string RemoveAltitudeSign(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var index = text.IndexOf(':');
            if (index < 0 || index + 1 >= text.Length)
                return TrimLeadingSign(text);

            return text.Substring(0, index + 1) + " " + TrimLeadingSign(text.Substring(index + 1).Trim());
        }

        string TrimLeadingSign(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text.StartsWith("+") || text.StartsWith("-") ? text.Substring(1) : text;
        }
    }
}

