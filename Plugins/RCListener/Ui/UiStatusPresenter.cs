using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner;

namespace RCListener.Ui
{
    public class UiStatusPresenter : IDisposable
    {
        private readonly Action<string> log;
        private readonly Action onRescanRequested;
        private readonly Color colorConnected = Color.FromArgb(0, 200, 0);
        private readonly Color colorDisconnected = Color.FromArgb(200, 0, 0);
        private readonly int statusIconSize = 20;

        private ToolStripButton rcStatusButton;
        private EventHandler clickHandler;

        public UiStatusPresenter(Action<string> log, Action onRescanRequested)
        {
            this.log = log ?? (_ => { });
            this.onRescanRequested = onRescanRequested ?? (() => { });
        }

        public void Initialize()
        {
            try
            {
                var form = MainV2.instance;
                if (form == null)
                {
                    log("[UI] Main form is not available, skipping status button init");
                    return;
                }

                rcStatusButton = new ToolStripButton
                {
                    Name = "RCLinkStatus",
                    Text = "RC LINK",
                    TextAlign = ContentAlignment.BottomCenter,
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                    Image = CreateStatusIcon(colorDisconnected),
                    ToolTipText = "RadioMaster link status (click to rescan ports)"
                };

                clickHandler = (s, e) => onRescanRequested();
                rcStatusButton.Click += clickHandler;

                var idx = form.MainMenu.Items.IndexOfKey("MenuHelp");
                if (idx < 0) idx = form.MainMenu.Items.Count - 1;
                form.MainMenu.Items.Insert(idx, rcStatusButton);

                log("[UI] RC LINK indicator added to menu");
            }
            catch (Exception ex)
            {
                log($"[UI] Failed to init status button: {ex.Message}");
            }
        }

        public void SetConnected(bool connected)
        {
            if (rcStatusButton == null)
                return;

            try
            {
                var color = connected ? colorConnected : colorDisconnected;

                Action update = () =>
                {
                    rcStatusButton.Image?.Dispose();
                    rcStatusButton.Image = CreateStatusIcon(color);
                };

                var form = MainV2.instance;
                if (form != null && form.InvokeRequired)
                    form.BeginInvoke(update);
                else
                    update();
            }
            catch (Exception ex)
            {
                log($"[UI] UpdateStatusButton error: {ex.Message}");
            }
        }

        private Bitmap CreateStatusIcon(Color color)
        {
            var bmp = new Bitmap(statusIconSize, statusIconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 2, 2, statusIconSize - 4, statusIconSize - 4);
            }
            return bmp;
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

                    rcStatusButton.Image?.Dispose();
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
            }
        }
    }
}