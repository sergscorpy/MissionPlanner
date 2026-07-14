using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class DroneInfoPanel : Control
    {
        const string PositionXSettingKey = "situationmarkers_drone_info_x";
        const string PositionYSettingKey = "situationmarkers_drone_info_y";
        const string SnapXSettingKey = "situationmarkers_drone_info_snap_x";
        const string SnapYSettingKey = "situationmarkers_drone_info_snap_y";
        const string DroneBearingPrefix = "BRG|";
        const string DroneSpeedPrefix = "SPD|";
        const string DroneDistancePrefix = "DST|";
        const string DroneEtaPrefix = "ETA|";
        const int EdgeMargin = 12;
        const int TopInterfaceInset = 42;
        const int BottomInterfaceInset = 52;
        const int SnapDistance = 50;

        string infoText = "";
        bool dragging;
        Point dragStartScreenMouse;
        Point dragStartLocation;
        bool positionLoaded;
        SnapSide snapX = SnapSide.None;
        SnapSide snapY = SnapSide.None;

        public DroneInfoPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint, true);

            Cursor = Cursors.SizeAll;
            BackColor = Color.Transparent;
            Visible = false;
        }

        public string InfoText
        {
            get { return infoText; }
            set
            {
                SetInfoText(value);
            }
        }

        public bool PanelEnabled { get; set; } = true;

        public void SetInfoText(string value)
        {
            value = value ?? "";

            if (infoText == value)
                return;

            infoText = value;
            Visible = PanelEnabled && infoText.Length > 0;
            UpdatePanelSize();
            EnsurePosition();
            Invalidate();
        }

        public void SetPanelEnabled(bool enabled)
        {
            PanelEnabled = enabled;
            Visible = PanelEnabled && infoText.Length > 0;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            if (Parent != null)
            {
                Parent.Resize -= Parent_Resize;
                Parent.Resize += Parent_Resize;
                EnsurePosition();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            dragging = true;
            dragStartScreenMouse = MousePosition;
            dragStartLocation = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!dragging)
                return;

            var screenMouse = MousePosition;
            Location = new Point(
                dragStartLocation.X + screenMouse.X - dragStartScreenMouse.X,
                dragStartLocation.Y + screenMouse.Y - dragStartScreenMouse.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
                return;

            dragging = false;
            Capture = false;
            Location = SnapToEdges(Location, true);
            SavePosition();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var lines = infoText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var font = CreateLabelFont())
            {
                var layout = MeasureLayout(e.Graphics, font, lines);
                var rect = new Rectangle(1, 1, Width - 2, Height - 2);
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

                    e.Graphics.FillPath(background, bubblePath);
                    e.Graphics.DrawPath(stroke, bubblePath);

                    if (lines.Length > 1)
                        DrawSeparators(e.Graphics, rect, layout, lines.Length, theme.Stroke);

                    for (var i = 0; i < lines.Length; i++)
                        DrawLine(e.Graphics, rect, layout, font, format, lines[i], i);
                }
            }
        }

        void DrawLine(Graphics graphics, Rectangle rect, LabelLayout layout, Font font, StringFormat format, string line, int index)
        {
            var iconKind = GetNavigationIconKind(line);
            var isNavigationLine = iconKind.Length > 0;
            var displayText = GetDisplayText(line);
            var direction = isNavigationLine ? 0 : GetRelativeDirection(ExtractAltitudeValue(line));
            var color = GetLabelTheme(direction).Stroke;
            var lineTop = rect.Y + layout.PaddingY + index * (layout.LineHeight + layout.LineSpacing);
            var iconBounds = new RectangleF(
                rect.X + layout.PaddingX,
                lineTop + (layout.LineHeight - 17) / 2f,
                17,
                17);
            var textRect = new RectangleF(
                rect.X + layout.PaddingX + layout.IconWidth + layout.ContentGap,
                lineTop,
                rect.Width - layout.PaddingX * 2 - layout.IconWidth - layout.ContentGap,
                layout.LineHeight);

            if (isNavigationLine)
                DrawNavigationIcon(graphics, iconBounds, iconKind, color);
            else if (direction != 0)
                DrawAltitudeDirectionIcon(graphics, iconBounds, direction, color);

            if (iconKind == "bearing")
            {
                DrawBearingText(graphics, displayText, font, textRect, format, color);
                return;
            }

            using (var foreground = new SolidBrush(color))
                graphics.DrawString(displayText, font, foreground, textRect, format);
        }

        void UpdatePanelSize()
        {
            using (var graphics = CreateGraphics())
            using (var font = CreateLabelFont())
            {
                var lines = infoText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                {
                    Size = Size.Empty;
                    return;
                }

                var layout = MeasureLayout(graphics, font, lines);
                Size = new Size(layout.Width + 2, layout.Height + 2);
            }
        }

        LabelLayout MeasureLayout(Graphics graphics, Font font, string[] lines)
        {
            var layout = new LabelLayout
            {
                PaddingX = 7,
                PaddingY = 5,
                LineHeight = (int)Math.Ceiling(graphics.MeasureString("Target: +000 m", font).Height),
                LineSpacing = lines.Length > 1 ? 5 : 0,
                IconWidth = 17,
                ContentGap = 4
            };

            var textWidth = 0;
            foreach (var line in lines)
                textWidth = Math.Max(textWidth,
                    (int)Math.Ceiling(graphics.MeasureString(GetDisplayText(line), font).Width));

            layout.Width = textWidth + layout.PaddingX * 2 + layout.IconWidth + layout.ContentGap + 4;
            layout.Height = layout.LineHeight * lines.Length +
                            layout.LineSpacing * (lines.Length - 1) +
                            layout.PaddingY * 2;
            return layout;
        }

        void EnsurePosition()
        {
            if (Parent == null || Width == 0 || Height == 0)
                return;

            if (!positionLoaded)
            {
                positionLoaded = true;
                LoadSnapState();
                Location = ApplySavedSnap(LoadPosition());
            }
        }

        Point LoadPosition()
        {
            if (Settings.Instance.ContainsKey(PositionXSettingKey) &&
                Settings.Instance.ContainsKey(PositionYSettingKey) &&
                int.TryParse(Settings.Instance[PositionXSettingKey], out var x) &&
                int.TryParse(Settings.Instance[PositionYSettingKey], out var y))
            {
                return new Point(x, y);
            }

            return new Point(GetSafeLeft(), GetSafeTop());
        }

        void SavePosition()
        {
            Settings.Instance[PositionXSettingKey] = Location.X.ToString();
            Settings.Instance[PositionYSettingKey] = Location.Y.ToString();
            Settings.Instance[SnapXSettingKey] = snapX.ToString();
            Settings.Instance[SnapYSettingKey] = snapY.ToString();
        }

        void Parent_Resize(object sender, EventArgs e)
        {
            if (dragging || Parent == null || Width == 0 || Height == 0)
                return;

            Location = ApplySavedSnap(Location);
            SavePosition();
        }

        void LoadSnapState()
        {
            snapX = LoadSnapSide(SnapXSettingKey);
            snapY = LoadSnapSide(SnapYSettingKey);
        }

        SnapSide LoadSnapSide(string key)
        {
            if (Settings.Instance.ContainsKey(key) &&
                Enum.TryParse(Settings.Instance[key], out SnapSide side))
                return side;

            return SnapSide.None;
        }

        Point SnapToEdges(Point location, bool updateSnapState)
        {
            if (Parent == null)
                return location;

            var minX = GetSafeLeft();
            var minY = GetSafeTop();
            var maxX = GetSafeRight(minX);
            var maxY = GetSafeBottom(minY);

            var x = location.X;
            var y = location.Y;
            var nextSnapX = SnapSide.None;
            var nextSnapY = SnapSide.None;

            if (x < minX || Math.Abs(x - minX) <= SnapDistance)
            {
                x = minX;
                nextSnapX = SnapSide.Min;
            }
            else if (x > maxX || Math.Abs(x - maxX) <= SnapDistance)
            {
                x = maxX;
                nextSnapX = SnapSide.Max;
            }

            if (y < minY || Math.Abs(y - minY) <= SnapDistance)
            {
                y = minY;
                nextSnapY = SnapSide.Min;
            }
            else if (y > maxY || Math.Abs(y - maxY) <= SnapDistance)
            {
                y = maxY;
                nextSnapY = SnapSide.Max;
            }

            if (updateSnapState)
            {
                snapX = nextSnapX;
                snapY = nextSnapY;
            }

            return new Point(x, y);
        }

        Point ApplySavedSnap(Point location)
        {
            if (Parent == null)
                return location;

            var minX = GetSafeLeft();
            var minY = GetSafeTop();
            var maxX = GetSafeRight(minX);
            var maxY = GetSafeBottom(minY);
            var x = location.X;
            var y = location.Y;

            if (snapX == SnapSide.Min)
                x = minX;
            else if (snapX == SnapSide.Max)
                x = maxX;

            if (snapY == SnapSide.Min)
                y = minY;
            else if (snapY == SnapSide.Max)
                y = maxY;

            return ClampToMap(new Point(x, y));
        }

        Point ClampToMap(Point location)
        {
            if (Parent == null)
                return location;

            var minX = GetSafeLeft();
            var minY = GetSafeTop();
            var maxX = GetSafeRight(minX);
            var maxY = GetSafeBottom(minY);

            return new Point(
                Math.Max(minX, Math.Min(maxX, location.X)),
                Math.Max(minY, Math.Min(maxY, location.Y)));
        }

        int GetSafeLeft()
        {
            return EdgeMargin;
        }

        int GetSafeTop()
        {
            return Math.Max(EdgeMargin, TopInterfaceInset);
        }

        int GetSafeRight(int minX)
        {
            return Parent == null
                ? minX
                : Math.Max(minX, Parent.ClientSize.Width - Width - EdgeMargin);
        }

        int GetSafeBottom(int minY)
        {
            return Parent == null
                ? minY
                : Math.Max(minY, Parent.ClientSize.Height - Height - BottomInterfaceInset);
        }

        enum SnapSide
        {
            None,
            Min,
            Max
        }

        Font CreateLabelFont()
        {
            return new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        void DrawSeparators(Graphics graphics, Rectangle rect, LabelLayout layout, int lineCount, Color color)
        {
            using (var pen = new Pen(Color.FromArgb(95, color), 1))
            {
                for (var i = 1; i < lineCount; i++)
                {
                    var y = rect.Y + layout.PaddingY + i * layout.LineHeight +
                            (i - 1) * layout.LineSpacing + layout.LineSpacing / 2f;
                    graphics.DrawLine(pen, rect.X + layout.PaddingX, y, rect.Right - layout.PaddingX, y);
                }
            }
        }

        void DrawBearingText(Graphics graphics, string text, Font font, RectangleF textRect, StringFormat format, Color color)
        {
            var bracketStart = text.IndexOf("(", StringComparison.Ordinal);
            if (bracketStart < 0)
            {
                using (var foreground = new SolidBrush(color))
                    graphics.DrawString(text, font, foreground, textRect, format);
                return;
            }

            var primaryText = text.Substring(0, bracketStart).TrimEnd();
            var homeText = text.Substring(bracketStart);

            using (var primary = new SolidBrush(color))
            using (var home = new SolidBrush(Color.Firebrick))
            {
                graphics.DrawString(primaryText, font, primary, textRect, format);

                var primaryWidth = (float)Math.Ceiling(graphics.MeasureString(primaryText + " ", font).Width);
                var homeRect = new RectangleF(
                    textRect.X + primaryWidth,
                    textRect.Y,
                    Math.Max(1, textRect.Width - primaryWidth),
                    textRect.Height);
                graphics.DrawString(homeText, font, home, homeRect, format);
            }
        }

        string GetDisplayText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            if (text.StartsWith(DroneBearingPrefix))
                return text.Substring(DroneBearingPrefix.Length);

            if (text.StartsWith(DroneSpeedPrefix))
                return text.Substring(DroneSpeedPrefix.Length);

            if (text.StartsWith(DroneDistancePrefix))
                return text.Substring(DroneDistancePrefix.Length);

            if (text.StartsWith(DroneEtaPrefix))
                return text.Substring(DroneEtaPrefix.Length);

            return RemoveAltitudeSign(text);
        }

        string GetNavigationIconKind(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            if (text.StartsWith(DroneBearingPrefix))
                return "bearing";

            if (text.StartsWith(DroneSpeedPrefix))
                return "speed";

            if (text.StartsWith(DroneDistancePrefix))
                return "distance";

            if (text.StartsWith(DroneEtaPrefix))
                return "eta";

            return "";
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

        void DrawAltitudeDirectionIcon(Graphics graphics, RectangleF bounds, int direction, Color color)
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
                    graphics.FillPolygon(brush, up);
                    graphics.DrawPolygon(pen, up);
                    graphics.DrawPolygon(pen, down);
                    return;
                }

                graphics.DrawPolygon(pen, up);
                graphics.FillPolygon(brush, down);
                graphics.DrawPolygon(pen, down);
            }
        }

        void DrawNavigationIcon(Graphics graphics, RectangleF bounds, string iconKind, Color color)
        {
            using (var pen = new Pen(color, 1.5f))
            using (var brush = new SolidBrush(color))
            {
                pen.LineJoin = LineJoin.Round;

                if (iconKind == "bearing")
                {
                    var centerX = bounds.X + bounds.Width / 2f;
                    var top = bounds.Y + 2;
                    var bottom = bounds.Bottom - 2;
                    var arrow = new PointF[]
                    {
                        new PointF(centerX, top),
                        new PointF(centerX + 5, bottom),
                        new PointF(centerX, bottom - 3),
                        new PointF(centerX - 5, bottom)
                    };
                    graphics.FillPolygon(brush, arrow);
                    graphics.DrawPolygon(pen, arrow);
                    return;
                }

                if (iconKind == "speed")
                {
                    graphics.DrawLine(pen, bounds.X + 2, bounds.Y + 5, bounds.Right - 4, bounds.Y + 5);
                    graphics.DrawLine(pen, bounds.Right - 4, bounds.Y + 5, bounds.Right - 8, bounds.Y + 2);
                    graphics.DrawLine(pen, bounds.Right - 4, bounds.Y + 5, bounds.Right - 8, bounds.Y + 8);
                    graphics.DrawLine(pen, bounds.X + 4, bounds.Y + 11, bounds.Right - 2, bounds.Y + 11);
                    graphics.DrawLine(pen, bounds.Right - 2, bounds.Y + 11, bounds.Right - 6, bounds.Y + 8);
                    graphics.DrawLine(pen, bounds.Right - 2, bounds.Y + 11, bounds.Right - 6, bounds.Y + 14);
                    return;
                }

                if (iconKind == "distance")
                {
                    var y = bounds.Y + bounds.Height / 2f;
                    graphics.DrawLine(pen, bounds.X + 3, y, bounds.Right - 3, y);
                    graphics.FillEllipse(brush, bounds.X + 1, y - 2, 4, 4);
                    graphics.FillEllipse(brush, bounds.Right - 5, y - 2, 4, 4);
                    return;
                }

                if (iconKind == "eta")
                {
                    var clock = new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4);
                    graphics.DrawEllipse(pen, clock);
                    graphics.DrawLine(pen, bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f,
                        bounds.X + bounds.Width / 2f, bounds.Y + 5);
                    graphics.DrawLine(pen, bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f,
                        bounds.Right - 5, bounds.Y + bounds.Height / 2f);
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

            return text[0] == '+' || text[0] == '-' ? text.Substring(1) : text;
        }

        struct LabelLayout
        {
            public int PaddingX;
            public int PaddingY;
            public int LineHeight;
            public int LineSpacing;
            public int IconWidth;
            public int ContentGap;
            public int Width;
            public int Height;
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
    }
}
