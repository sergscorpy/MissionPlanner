using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MissionPlanner;
using RCListener.Logging;
using RCListener.Control;

namespace RCListener.Ui
{
    public class UiStatusPresenter : IDisposable
    {
        private readonly ILogger log;
        private readonly Action onClick;
        private readonly string statusIconDir =
            Path.Combine(
                Path.GetDirectoryName(typeof(UiStatusPresenter).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory,
                "Ui",
                "Image");

        private ToolStripButton rcStatusButton;
        private EventHandler clickHandler;
        private Image connectedIcon;
        private Image waitingIcon;
        private Image disconnectedIcon;

        public UiStatusPresenter(ILogger log, Action onClick)
        {
            this.log = log;
            this.onClick = onClick ?? (() => { });
        }

        public void Initialize()
        {
            try
            {
                var form = MainV2.instance;
                if (form == null)
                {
                    log.Log("[UI] Main form is not available, skipping status button init");
                    return;
                }

                EnsureIconsLoaded();

                rcStatusButton = new ToolStripButton
                {
                    Name = "RCLinkStatus",
                    Text = "RC LINK",
                    TextAlign = ContentAlignment.BottomCenter,
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                    Image = disconnectedIcon,
                    ToolTipText = "RadioMaster link status (click to open RC tab)"
                };

                clickHandler = (s, e) => onClick();
                rcStatusButton.Click += clickHandler;

                var idx = form.MainMenu.Items.IndexOfKey("MenuHelp");
                if (idx < 0) idx = form.MainMenu.Items.Count - 1;
                form.MainMenu.Items.Insert(idx, rcStatusButton);

                log.Log("[UI] RC LINK indicator added to menu");
            }
            catch (Exception ex)
            {
                log.Log($"[UI] Failed to init status button: {ex.Message}");
            }
        }

        public void SetConnectionState(RcListenerController.ConnectionState state)
        {
            var button = rcStatusButton;
            if (button == null)
                return;

            try
            {
                var icon = GetIconForState(state);

                Action update = () =>
                {
                    if (button == null || button.IsDisposed)
                        return;

                    button.Image = icon;
                };

                var form = MainV2.instance;
                if (form != null && form.InvokeRequired)
                    form.BeginInvoke(update);
                else
                    update();
            }
            catch (Exception ex)
            {
                log.Log($"[UI] UpdateStatusButton error: {ex.Message}");
            }
        }

        public void SetScanning(bool scanning)
        {
            var button = rcStatusButton;
            if (button == null)
                return;

            try
            {
                Action update = () =>
                {
                    if (button == null || button.IsDisposed)
                        return;

                    button.ToolTipText = scanning
                        ? "RadioMaster link: scanning ports... (click to open RC tab)"
                        : "RadioMaster link status (click to open RC tab)";
                };

                var form = MainV2.instance;
                if (form != null && form.InvokeRequired)
                    form.BeginInvoke(update);
                else
                    update();
            }
            catch (Exception ex)
            {
                log.Log($"[UI] UpdateScanState error: {ex.Message}");
            }
        }

        private void EnsureIconsLoaded()
        {
            if (connectedIcon != null && waitingIcon != null && disconnectedIcon != null)
                return;

            connectedIcon = LoadIcon("Joystick_green.png");
            waitingIcon = LoadIcon("Joystick_yellow.png");
            disconnectedIcon = LoadIcon("Joystick_red.png");
        }

        private Image GetIconForState(RcListenerController.ConnectionState state)
        {
            switch (state)
            {
                case RcListenerController.ConnectionState.Connected:
                    return connectedIcon;
                case RcListenerController.ConnectionState.WaitingForHandshake:
                    return waitingIcon;
                default:
                    return disconnectedIcon;
            }
        }

        private Image LoadIcon(string fileName)
        {
            var path = Path.Combine(statusIconDir, fileName);
            if (!File.Exists(path))
            {
                log.Log($"[UI] Status icon missing: {path}");
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                {
                    return new Bitmap(img);
                }
            }
            catch (Exception ex)
            {
                log.Log($"[UI] Failed to load status icon {fileName}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (rcStatusButton != null)
                {
                    if (clickHandler != null)
                        rcStatusButton.Click -= clickHandler;

                    var form = MainV2.instance;
                    if (form != null)
                    {
                        try
                        {
                            if (form.InvokeRequired)
                                form.BeginInvoke((Action)(() => form.MainMenu.Items.Remove(rcStatusButton)));
                            else
                                form.MainMenu.Items.Remove(rcStatusButton);
                        }
                        catch
                        {
                        }
                    }

                    rcStatusButton.Dispose();
                }
            }
            catch
            {
            }
            finally
            {
                rcStatusButton = null;
                clickHandler = null;
                connectedIcon?.Dispose();
                waitingIcon?.Dispose();
                disconnectedIcon?.Dispose();
                connectedIcon = null;
                waitingIcon = null;
                disconnectedIcon = null;
            }
        }
    }
}