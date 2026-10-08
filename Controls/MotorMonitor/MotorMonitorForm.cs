using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.MotorMonitor
{
    internal sealed class MotorMonitorForm : Form
    {
        private MAVState vehicle;
        private MotorLayout layout;
        private MotorDiagram diagram;
        private readonly Label waiting;
        private readonly CheckBox pwm, rpm, voltage, current, bars;
        private readonly Label warning;
        private readonly Label disconnected;
        private readonly Font disconnectedFont;
        private readonly Timer refreshTimer;
        private readonly FlowLayoutPanel options;
        private readonly TableLayoutPanel root;
        private bool fittingWidth;
        private bool userResizing;
        private bool widthFitQueued;
        private Rectangle resizeStartBounds;
        private int resizeNonDiagramHeight;
        private float resizeHeightPerWidth;
        private Size lastNormalClientSize;

        private const int WmSizing = 0x0214;
        private const string SettingPrefix = "MotorMonitor_";

        private sealed class ConnectionStatusLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                // Keep the status red even when MP reapplies its theme to the window.
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.Red,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRectangle
        {
            public int Left, Top, Right, Bottom;
        }

        public MotorMonitorForm()
        {
            Text = "Монітор двигунів — очікування даних";
            AutoScaleMode = AutoScaleMode.Dpi;
            MaximizeBox = false;
            DoubleBuffered = true;
            ResizeRedraw = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(300, 100);
            StartPosition = FormStartPosition.CenterParent;

            root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Visible = false };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8), WrapContents = false };
            pwm = new CheckBox { Text = "PWM", Checked = Settings.Instance.GetBoolean(SettingPrefix + "PWM", true), AutoSize = true };
            rpm = new CheckBox { Text = "RPM", Checked = Settings.Instance.GetBoolean(SettingPrefix + "RPM", true), AutoSize = true };
            voltage = new CheckBox { Text = "Напруга, V", Checked = Settings.Instance.GetBoolean(SettingPrefix + "Voltage", true), AutoSize = true };
            current = new CheckBox { Text = "Струм, A", Checked = Settings.Instance.GetBoolean(SettingPrefix + "Current", true), AutoSize = true };
            bars = new CheckBox { Text = "Діаграма", Checked = Settings.Instance.GetBoolean(SettingPrefix + "Bars", false), AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
            options.Controls.AddRange(new Control[] { pwm, rpm, voltage, current, bars });
            warning = new Label { Dock = DockStyle.Fill, AutoSize = true, Visible = false, Padding = new Padding(10, 4, 10, 4) };
            root.Controls.Add(options, 0, 0);
            root.Controls.Add(warning, 0, 1);
            Controls.Add(root);
            waiting = new Label { Dock = DockStyle.Fill, Text = "Очікування підключення дрона…",
                TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12) };
            Controls.Add(waiting);
            disconnected = new ConnectionStatusLabel
            {
                Text = "Disconnected", AutoSize = false, Visible = false,
                TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(disconnected);
            Utilities.ThemeManager.ApplyThemeTo(this);
            warning.ForeColor = Color.DarkOrange;
            disconnected.ForeColor = Color.Red;
            disconnectedFont = new Font(pwm.Font, FontStyle.Bold);
            disconnected.Font = disconnectedFont;
            EventHandler alignStatus = (s, e) =>
            {
                int width = TextRenderer.MeasureText(disconnected.Text, disconnected.Font).Width;
                disconnected.Size = new Size(width, pwm.Height);
                disconnected.Location = new Point(ClientSize.Width - width -
                    (int)Math.Round(8 * DeviceDpi / 96f), root.Top + options.Top + pwm.Top);
                disconnected.BringToFront();
            };
            Layout += (s, e) => alignStatus(s, e);
            options.Layout += (s, e) => alignStatus(s, e);

            EventHandler updateOptions = (s, e) =>
            {
                ApplyOptions();
                Settings.Instance[SettingPrefix + "PWM"] = pwm.Checked.ToString();
                Settings.Instance[SettingPrefix + "RPM"] = rpm.Checked.ToString();
                Settings.Instance[SettingPrefix + "Voltage"] = voltage.Checked.ToString();
                Settings.Instance[SettingPrefix + "Current"] = current.Checked.ToString();
                Settings.Instance[SettingPrefix + "Bars"] = bars.Checked.ToString();
                QueueFitContentWidth();
            };
            pwm.CheckedChanged += updateOptions;
            rpm.CheckedChanged += updateOptions;
            voltage.CheckedChanged += updateOptions;
            current.CheckedChanged += updateOptions;
            bars.CheckedChanged += updateOptions;
            updateOptions(this, EventArgs.Empty);
            refreshTimer = new Timer { Interval = 150 };
            refreshTimer.Tick += RefreshTelemetry;
            Shown += (s, e) =>
            {
                RefreshTelemetry(s, e);
                if (!IsDisposed)
                {
                    FitContentWidth();
                    QueueFitContentWidth();
                    refreshTimer.Start();
                }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshTelemetry(this, e);
        }

        private void ApplyOptions()
        {
            if (diagram == null)
                return;
            diagram.ShowPwm = pwm.Checked;
            diagram.ShowRpm = rpm.Checked;
            diagram.ShowVoltage = voltage.Checked;
            diagram.ShowCurrent = current.Checked;
            diagram.RingMode = !bars.Checked;
            diagram.Invalidate();
        }

        private void RestoreNormalSize()
        {
            float dpiScale = DeviceDpi / 96f;
            var screen = Screen.FromControl(this).WorkingArea;
            int width = (int)Math.Round(Settings.Instance.GetInt32(SettingPrefix + "Width", 540) * (double)dpiScale);
            int height = (int)Math.Round(Settings.Instance.GetInt32(SettingPrefix + "Height", 395) * (double)dpiScale);
            // Restore logical pixels at the current DPI; automatic fitting remains
            // authoritative for width when the frame or selected indicators changed.
            ClientSize = new Size(Math.Max(1, Math.Min(width, screen.Width - (Width - ClientSize.Width))),
                Math.Max(MinimumSize.Height - (Height - ClientSize.Height),
                    Math.Min(height, screen.Height - (Height - ClientSize.Height))));
            FitContentWidth();
            QueueFitContentWidth();
        }

        private int PreferredClientWidth(int diagramHeight = 0)
        {
            return Math.Max(diagram.GetContentWidth(diagramHeight),
                options.GetPreferredSize(Size.Empty).Width + options.Margin.Horizontal +
                TextRenderer.MeasureText(disconnected.Text, disconnected.Font).Width + (int)Math.Round(16 * DeviceDpi / 96f));
        }

        private void QueueFitContentWidth()
        {
            if (diagram == null || widthFitQueued || fittingWidth || userResizing || IsDisposed || Disposing || !IsHandleCreated)
                return;
            widthFitQueued = true;
            BeginInvoke(new Action(() =>
            {
                widthFitQueued = false;
                if (!IsDisposed && !Disposing)
                    FitContentWidth();
            }));
        }

        private void FitContentWidth()
        {
            if (diagram == null || fittingWidth || userResizing || IsDisposed || !IsHandleCreated || WindowState != FormWindowState.Normal)
                return;
            fittingWidth = true;
            try
            {
                // A warning may wrap differently after resizing, changing the diagram's
                // height. Re-measure after layout without recursively handling SizeChanged.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    PerformLayout();
                    root.PerformLayout();
                    int width = PreferredClientWidth();
                    if (width <= 0 || ClientSize.Width == width)
                        break;
                    ClientSize = new Size(width, ClientSize.Height);
                }
                // Flush the last resize too, even when the iteration limit was reached.
                PerformLayout();
                root.PerformLayout();
                diagram.Invalidate();
            }
            finally
            {
                fittingWidth = false;
                SaveWindowSize();
            }
        }

        private void SaveWindowSize()
        {
            // The compact waiting state must never replace the user's monitor size.
            if (diagram == null)
                return;
            if (WindowState == FormWindowState.Normal)
                lastNormalClientSize = ClientSize;
            if (lastNormalClientSize.IsEmpty)
                return;
            float dpiScale = DeviceDpi / 96f;
            Settings.Instance[SettingPrefix + "Width"] = ((int)Math.Round(lastNormalClientSize.Width / dpiScale)).ToString();
            Settings.Instance[SettingPrefix + "Height"] = ((int)Math.Round(lastNormalClientSize.Height / dpiScale)).ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel)
                SaveWindowSize();
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            if (diagram == null)
            {
                base.OnResizeBegin(e);
                return;
            }
            userResizing = true;
            resizeStartBounds = Bounds;
            resizeNonDiagramHeight = Height - diagram.ClientSize.Height;
            int widthIncrease = PreferredClientWidth(diagram.ClientSize.Height + 32) - PreferredClientWidth();
            // The toolbar may set a minimum width. In that flat region a horizontal
            // gesture still changes height, rather than leaving the handle unresponsive.
            resizeHeightPerWidth = widthIncrease > 0 ? 32f / widthIncrease : 1f;
            base.OnResizeBegin(e);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            userResizing = false;
            base.OnResizeEnd(e);
            FitContentWidth();
            QueueFitContentWidth();
            diagram?.Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmSizing && userResizing && diagram != null && options != null)
            {
                // Set the native proposed bounds once, before Windows resizes the form.
                // Do not fight interactive sizing with ClientSize changes in SizeChanged.
                var rectangle = Marshal.PtrToStructure<WindowRectangle>(message.LParam);
                int edge = message.WParam.ToInt32();
                bool side = edge == 1 || edge == 2;
                bool corner = edge == 4 || edge == 5 || edge == 7 || edge == 8;
                bool left = edge == 1 || edge == 4 || edge == 7;
                bool top = edge >= 3 && edge <= 5;
                float horizontalHeightChange = ((left ? resizeStartBounds.Left - rectangle.Left :
                    rectangle.Right - resizeStartBounds.Right)) * resizeHeightPerWidth;
                float verticalHeightChange = top ? resizeStartBounds.Top - rectangle.Top :
                    rectangle.Bottom - resizeStartBounds.Bottom;
                float heightChange = side || (corner && Math.Abs(horizontalHeightChange) > Math.Abs(verticalHeightChange))
                    ? horizontalHeightChange : verticalHeightChange;
                int height = Math.Max(MinimumSize.Height, resizeStartBounds.Height + (int)Math.Round(heightChange));
                int diagramHeight = Math.Max(1, height - resizeNonDiagramHeight);
                int width = PreferredClientWidth(diagramHeight) + Width - ClientSize.Width;
                rectangle.Top = top ? resizeStartBounds.Bottom - height : resizeStartBounds.Top;
                rectangle.Bottom = rectangle.Top + height;
                rectangle.Left = left ? resizeStartBounds.Right - width : resizeStartBounds.Left;
                rectangle.Right = rectangle.Left + width;
                Marshal.StructureToPtr(rectangle, message.LParam, false);
                message.Result = (IntPtr)1;
            }
        }

        private void RefreshTelemetry(object sender, EventArgs e)
        {
            var port = MainV2.comPort;
            var activeVehicle = port?.MAV;
            // Use the same three-second no-data threshold as MP's connection warning.
            // An open serial/UDP stream alone does not mean telemetry is arriving.
            bool receiving = port != null && (port.logreadmode ||
                (port.BaseStream?.IsOpen == true && activeVehicle != null &&
                 (DateTime.UtcNow - activeVehicle.lastvalidpacket).TotalSeconds <= 3));
            if (!receiving)
            {
                if (diagram == null)
                    ShowWaiting("Очікування підключення та даних дрона…");
                else
                    disconnected.Visible = true;
                // Preserve the last snapshot, even if MP clears the current vehicle on disconnect.
                return;
            }
            if (!MotorTelemetry.Parameter(activeVehicle, "FRAME_CLASS", out _) ||
                !MotorTelemetry.Parameter(activeVehicle, "FRAME_TYPE", out _))
            {
                if (diagram == null)
                    ShowWaiting("Очікування параметрів\nFRAME_CLASS та FRAME_TYPE…");
                else
                    disconnected.Visible = !ReferenceEquals(vehicle, activeVehicle);
                return;
            }
            if (!MotorTelemetry.TryLayout(activeVehicle, out var activeLayout, out var error))
            {
                ShowWaiting(error);
                return;
            }
            if (diagram == null || !ReferenceEquals(vehicle, activeVehicle) || !ReferenceEquals(layout, activeLayout))
                ShowMonitor(activeVehicle, activeLayout);
            disconnected.Visible = false;
            diagram.Snapshot = MotorTelemetry.Read(vehicle, layout);
            warning.Text = diagram.Snapshot.Warning;
            warning.Visible = !string.IsNullOrEmpty(warning.Text);
            diagram.Invalidate();
        }

        private void ShowWaiting(string message)
        {
            disconnected.Visible = false;
            waiting.Text = message;
            if (diagram == null)
                return;
            SaveWindowSize();
            root.Visible = false;
            var previousDiagram = diagram;
            diagram = null;
            root.Controls.Remove(previousDiagram);
            previousDiagram.Dispose();
            vehicle = null;
            layout = null;
            userResizing = false;
            waiting.Visible = true;
            waiting.BringToFront();
            Text = "Монітор двигунів — очікування даних";
            MinimumSize = Size.Empty;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            float dpiScale = DeviceDpi / 96f;
            ClientSize = new Size((int)(300 * dpiScale), (int)(100 * dpiScale));
        }

        private void ShowMonitor(MAVState activeVehicle, MotorLayout activeLayout)
        {
            if (diagram != null)
            {
                SaveWindowSize();
                root.Controls.Remove(diagram);
                diagram.Dispose();
            }
            vehicle = activeVehicle;
            layout = activeLayout;
            diagram = new MotorDiagram(layout) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            // Defer measuring until the table has completed its layout.
            diagram.SizeChanged += (s, e) => QueueFitContentWidth();
            ApplyOptions();
            root.Controls.Add(diagram, 0, 2);
            Utilities.ThemeManager.ApplyThemeTo(diagram);
            waiting.Visible = false;
            root.Visible = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(1, (int)Math.Round(360 * DeviceDpi / 96f));
            Text = "Монітор двигунів — " + layout.Name + " · SYS " + vehicle.sysid;
            RestoreNormalSize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer?.Dispose();
                disconnectedFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
