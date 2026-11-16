using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Controls.MotorMonitor
{
    internal sealed class MotorDiagram : Control
    {
        private readonly MotorLayout layout;
        private readonly Bitmap frameImage;
        private static readonly float[] ColorLevels = { 0, 30, 55, 80, 95 };
        private static readonly Color[] CommandColors =
        {
            Color.FromArgb(160, 64, 224), Color.DeepSkyBlue, Color.Lime, Color.Orange, Color.Red
        };
        public MotorSnapshot Snapshot { get; set; }
        public bool ShowPwm { get; set; } = true;
        public bool ShowRpm { get; set; } = true;
        public bool ShowVoltage { get; set; } = true;
        public bool ShowCurrent { get; set; } = true;
        public bool RingMode { get; set; } = true;

        public MotorDiagram(MotorLayout layout)
        {
            this.layout = layout;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var stream = typeof(MotorDiagram).Assembly.GetManifestResourceStream("MotorMonitor." + layout.Image))
            {
                if (stream == null)
                    throw new InvalidOperationException("Зображення схеми не знайдено: " + layout.Image);
                using (var image = Image.FromStream(stream))
                    frameImage = new Bitmap(image);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var geometry = MeasureContent(g, ClientSize.Height))
            {
                if (geometry == null)
                    return;
                // The same measured bounds drive painting and automatic window width.
                float offsetX = (ClientSize.Width - geometry.Bounds.Width) / 2 - geometry.Bounds.Left;
                float offsetY = (ClientSize.Height - geometry.Bounds.Height) / 2 - geometry.Bounds.Top;
                var imageRect = geometry.Image;
                imageRect.Offset(offsetX, offsetY);
                g.DrawImage(frameImage, imageRect);
                using (var text = new SolidBrush(ForeColor))
                using (var track = new SolidBrush(Color.FromArgb(80, ForeColor)))
                using (var rpmBrush = new SolidBrush(Color.Fuchsia))
                using (var voltageBrush = new SolidBrush(Color.Cyan))
                using (var currentBrush = new SolidBrush(Color.Red))
                {
                    for (int index = 0; index < layout.Motors.Length; index++)
                    {
                        var motor = layout.Motors[index];
                        var reading = Snapshot?.Motors?.FirstOrDefault(item => item.Number == motor.Number) ??
                            new MotorReading { Number = motor.Number };
                        float x = imageRect.Left + motor.X * imageRect.Width;
                        float y = imageRect.Top + motor.Y * imageRect.Height;
                        bool left = motor.X < 0.5f;
                        using (var commandBrush = new SolidBrush(CommandColor(reading.Command)))
                        {
                            if (RingMode)
                                DrawRing(g, x, y, layout.InnerRadius * imageRect.Width,
                                    layout.OuterRadius * imageRect.Width, reading.Command, commandBrush);

                            if (!RingMode)
                            {
                                var bar = geometry.Bars[index];
                                bar.Offset(offsetX, offsetY);
                                g.FillRectangle(track, bar);
                                float fillHeight = bar.Height * reading.Command / 100;
                                g.FillRectangle(commandBrush, bar.X, bar.Bottom - fillHeight, bar.Width, fillHeight);
                            }
                            if (geometry.Rows == 0)
                                continue;
                            var card = geometry.Cards[index];
                            card.Offset(offsetX, offsetY);
                            using (var format = new StringFormat(StringFormat.GenericTypographic)
                            {
                                Alignment = left ? StringAlignment.Far : StringAlignment.Near,
                                LineAlignment = StringAlignment.Center,
                                FormatFlags = StringFormatFlags.NoWrap
                            })
                            {
                                float rowHeight = card.Height * geometry.TextHeightFraction / Math.Max(2, geometry.Rows);
                                var row = new RectangleF(card.X, card.Y + (card.Height - rowHeight * geometry.Rows) / 2,
                                    card.Width, rowHeight);
                                if (geometry.ShowMotorName)
                                    DrawRow(g, "Mot " + motor.Number, geometry.LabelFont, text, format, ref row);
                                if (ShowPwm)
                                    DrawRow(g, $"{reading.Command:0}%", geometry.LabelFont, commandBrush, format, ref row);
                                if (ShowRpm)
                                    DrawRow(g, $"{reading.Rpm:0}", geometry.LabelFont, rpmBrush, format, ref row);
                                if (ShowVoltage)
                                    DrawRow(g, $"{reading.Voltage:0.0} V", geometry.LabelFont, voltageBrush, format, ref row);
                                if (ShowCurrent)
                                    DrawRow(g, $"{reading.Current:0.0} A", geometry.LabelFont, currentBrush, format, ref row);
                            }
                        }
                    }
                }
            }
        }

        private static void DrawRow(Graphics g, string value, Font font, Brush brush, StringFormat format, ref RectangleF row)
        {
            g.DrawString(value, font, brush, row, format);
            row.Y += row.Height;
        }

        private sealed class ContentGeometry : IDisposable
        {
            public RectangleF Image;
            public RectangleF Bounds;
            public RectangleF[] Cards;
            public RectangleF[] Bars;
            public Font LabelFont;
            public int Rows;
            public bool ShowMotorName;
            public float TextHeightFraction;
            public float Margin;

            public void Dispose() => LabelFont.Dispose();
        }

        public int GetContentWidth(int height = 0)
        {
            using (var g = CreateGraphics())
            using (var geometry = MeasureContent(g, height > 0 ? height : ClientSize.Height))
                return geometry == null ? 0 : (int)Math.Ceiling(geometry.Bounds.Width + 2 * geometry.Margin);
        }

        private ContentGeometry MeasureContent(Graphics g, int height)
        {
            if (height <= 0)
                return null;
            // Width never influences scale, so fitting the window cannot create a resize loop.
            float dpi = g.DpiX / 96f;
            float unit = dpi * Math.Max(0.6f, Math.Min(1.75f, height / (330f * dpi)));
            float margin = 6 * unit;
            float imageHeight = Math.Max(1, height - 2 * margin);
            float imageWidth = imageHeight * frameImage.Width / frameImage.Height;
            int indicators = (ShowPwm ? 1 : 0) + (ShowRpm ? 1 : 0) + (ShowVoltage ? 1 : 0) + (ShowCurrent ? 1 : 0);
            bool showMotorName = indicators >= 3;
            int rows = indicators + (showMotorName ? 1 : 0);
            float textHeightFraction = indicators <= 2 ? 0.8f : 1f;
            float cardHeight;
            using (var baseFont = new Font(Font.FontFamily, 14 * unit, FontStyle.Bold, GraphicsUnit.Pixel))
                cardHeight = 5 * (baseFont.GetHeight(g) + unit);
            var geometry = new ContentGeometry
            {
                Image = new RectangleF(0, 0, imageWidth, imageHeight),
                Bounds = new RectangleF(0, 0, imageWidth, imageHeight),
                Cards = new RectangleF[layout.Motors.Length],
                Bars = new RectangleF[layout.Motors.Length],
                LabelFont = new Font(Font.FontFamily, 14 * unit * 5f / Math.Max(2, rows) * textHeightFraction,
                    FontStyle.Bold, GraphicsUnit.Pixel),
                Rows = rows,
                ShowMotorName = showMotorName,
                TextHeightFraction = textHeightFraction,
                Margin = margin
            };
            float cardWidth = 0;
            if (rows > 0)
            {
                var samples = showMotorName ? layout.Motors.Select(motor => "Mot " + motor.Number).ToList() :
                    new System.Collections.Generic.List<string>();
                if (ShowPwm) samples.Add("100%");
                if (ShowRpm) samples.Add("65535");
                if (ShowVoltage) samples.Add($"{655.4:0.0} V");
                if (ShowCurrent) samples.Add($"{655.4:0.0} A");
                cardWidth = (float)Math.Ceiling(samples.Max(value =>
                    g.MeasureString(value, geometry.LabelFont, int.MaxValue, StringFormat.GenericTypographic).Width) + 4 * unit);
            }
            float motorRadius = layout.OutlineRadius * imageWidth;
            float barWidth = 15 * unit;
            for (int index = 0; index < layout.Motors.Length; index++)
            {
                var motor = layout.Motors[index];
                float x = motor.X * imageWidth;
                float y = motor.Y * imageHeight;
                bool left = motor.X < 0.5f;
                float edge = x + (left ? -motorRadius - margin : motorRadius + margin);
                if (!RingMode)
                {
                    var bar = new RectangleF(left ? edge - barWidth : edge, y - cardHeight / 2, barWidth, cardHeight);
                    geometry.Bars[index] = bar;
                    geometry.Bounds = RectangleF.Union(geometry.Bounds, bar);
                    edge = left ? bar.Left - margin : bar.Right + margin;
                }
                if (rows > 0)
                {
                    var card = new RectangleF(left ? edge - cardWidth : edge, y - cardHeight / 2, cardWidth, cardHeight);
                    geometry.Cards[index] = card;
                    geometry.Bounds = RectangleF.Union(geometry.Bounds, card);
                }
            }
            return geometry;
        }

        internal static Color CommandColor(float percent)
        {
            if (float.IsNaN(percent) || percent <= ColorLevels[0])
                return CommandColors[0];
            for (int i = 1; i < ColorLevels.Length; i++)
            {
                if (percent > ColorLevels[i])
                    continue;
                float blend = (percent - ColorLevels[i - 1]) / (ColorLevels[i] - ColorLevels[i - 1]);
                var start = CommandColors[i - 1];
                var end = CommandColors[i];
                return Color.FromArgb(
                    (int)Math.Round(start.R + (end.R - start.R) * blend),
                    (int)Math.Round(start.G + (end.G - start.G) * blend),
                    (int)Math.Round(start.B + (end.B - start.B) * blend));
            }
            return CommandColors[CommandColors.Length - 1];
        }

        private static void DrawRing(Graphics g, float x, float y, float inner, float outer, float command, Brush brush)
        {
            if (command <= 0)
                return;
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                var outside = new RectangleF(x - outer, y - outer, outer * 2, outer * 2);
                var inside = new RectangleF(x - inner, y - inner, inner * 2, inner * 2);
                if (command >= 100)
                {
                    path.AddEllipse(outside);
                    path.AddEllipse(inside);
                }
                else
                {
                    float sweep = command * 3.6f;
                    path.AddArc(outside, -90, sweep);
                    path.AddArc(inside, -90 + sweep, -sweep);
                    path.CloseFigure();
                }
                g.FillPath(brush, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                frameImage.Dispose();
            base.Dispose(disposing);
        }
    }
}
