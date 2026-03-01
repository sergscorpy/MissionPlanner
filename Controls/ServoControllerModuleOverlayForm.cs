using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using log4net;

namespace MissionPlanner.Controls
{
    public class ServoControllerModuleOverlayForm : Form
    {
        private const int WsExNoActivate = 0x08000000;

        private static readonly ILog log =
    LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private const int MaxIconsCount = 6;
        private const int DefaultVisibleIconsCount = 4;
        private const int MinRcChannel = 5;
        private const int MaxRcChannel = 16;
        private const int CommandActivationThreshold = 1700;
        private const int SafetyActivationThreshold = 1700;
        private const int BaseFormWidth = 150;
        private const int BaseFormHeight = 360;
        private const string PositionXSettingKey = "ServoControllerModuleOverlayForm.PositionX";
        private const string PositionYSettingKey = "ServoControllerModuleOverlayForm.PositionY";

        private readonly TableLayoutPanel iconsLayout;
        private readonly PictureBox safetyIcon;
        private readonly List<PictureBox> icons = new List<PictureBox>();
        private readonly List<ToolStripMenuItem> iconCountMenuItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> commandChannelMenuItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> safetyChannelMenuItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> selectionChannelMenuItems = new List<ToolStripMenuItem>();
        private readonly Timer blinkTimer;
        private readonly Image unlockedImage;
        private readonly Image lockedImage;
        private readonly Image unlockedOrangeImage;
        private readonly Image lockedOrangeImage;
        private readonly Image unlockedRedImage;
        private readonly Image lockedRedImage;
        private readonly Image safeOnImage;
        private readonly Image safeOffImage;

        private bool isDragging;
        private Point dragOffset;
        private int visibleIconsCount = DefaultVisibleIconsCount;
        private int lockMask;
        private int commandChannel = 5;
        private int safetyChannel = 6;
        private int selectionChannel = 7;
        private readonly ushort[] rcChannelValues = new ushort[18];
        private bool safetyActive;
        private bool commandActive;
        private int selectedIconIndex = -1;
        private bool blinkIsRed;
        private int lastSelectionChannelValue = -1;
        private readonly int baseNonClientHeight;
        private readonly int iconRowHeight;

        public ServoControllerModuleOverlayForm()
        {
            Text = "Плата контролю";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(BaseFormWidth, BaseFormHeight);

            iconsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = MaxIconsCount + 1,
                Padding = new Padding(20, 15, 20, 15)
            };

            for (var i = 0; i < iconsLayout.RowCount; i++)
            {
                iconsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / iconsLayout.RowCount));
            }

            unlockedImage = Image.FromFile(ResolveIconImagePath("Drops_Empty.png"));
            lockedImage = Image.FromFile(ResolveIconImagePath("Drops_Green.png"));
            unlockedOrangeImage = Image.FromFile(ResolveIconImagePath("Drops_Empty_Orange.png"));
            lockedOrangeImage = Image.FromFile(ResolveIconImagePath("Drops_Orange.png"));
            unlockedRedImage = Image.FromFile(ResolveIconImagePath("Drops_Empty_Red.png"));
            lockedRedImage = Image.FromFile(ResolveIconImagePath("Drops_Red.png"));
            safeOnImage = Image.FromFile(ResolveIconImagePath("Safe_On.png"));
            safeOffImage = Image.FromFile(ResolveIconImagePath("Safe_Off.png"));

            safetyIcon = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = safeOffImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 4, 0, 4)
            };

            iconsLayout.Controls.Add(safetyIcon, 0, 0);

            for (var i = 0; i < MaxIconsCount; i++)
            {
                var icon = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = unlockedImage,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(0, 4, 0, 4)
                };

                icons.Add(icon);
                iconsLayout.Controls.Add(icon, 0, i + 1);
            }

            Controls.Add(iconsLayout);

            baseNonClientHeight = Height - ClientSize.Height;
            var availableContentHeight = Math.Max(1, ClientSize.Height - iconsLayout.Padding.Vertical);
            iconRowHeight = Math.Max(1, availableContentHeight / (DefaultVisibleIconsCount + 1));

            blinkTimer = new Timer { Interval = 500 };
            blinkTimer.Tick += (_, __) =>
            {
                blinkIsRed = !blinkIsRed;
                ApplyOverlayState();
            };

            ContextMenuStrip = BuildContextMenu();
            RegisterDragEvents(this);
            LoadSavedPosition();
            ApplyVisibleIconsCount();
            ApplyOverlayState();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var createParams = base.CreateParams;
                createParams.ExStyle |= WsExNoActivate;
                return createParams;
            }
        }

        public void UpdateLockMask(int newLockMask)
        {
            lockMask = newLockMask;

            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)ApplyOverlayState);
                return;
            }

            ApplyOverlayState();
        }

        public void UpdateRcChannels(ushort[] channels)
        {
            if (channels == null)
            {
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateRcChannels(channels)));
                return;
            }

            var count = Math.Min(rcChannelValues.Length, channels.Length);
            Array.Copy(channels, rcChannelValues, count);

            UpdateStateFromRcChannels();
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var iconsCountItem = new ToolStripMenuItem("Кількість іконок");
            for (var i = 1; i <= MaxIconsCount; i++)
            {
                var count = i;
                var item = new ToolStripMenuItem($"Іконка {count}")
                {
                    CheckOnClick = true
                };

                item.Click += (_, __) =>
                {
                    visibleIconsCount = count;
                    ApplyVisibleIconsCount();
                };

                iconCountMenuItems.Add(item);
                iconsCountItem.DropDownItems.Add(item);
            }

            var commandChannelItem = BuildRcChannelMenuItem("Канал команди", commandChannelMenuItems, channel =>
            {
                commandChannel = channel;
                UpdateStateFromRcChannels();
            });

            var safetyChannelItem = BuildRcChannelMenuItem("Канал запобіжника", safetyChannelMenuItems, channel =>
            {
                safetyChannel = channel;
                log.Info($"Обрано канал запобіжника: CH{safetyChannel}");
                UpdateStateFromRcChannels();
            });

            var selectionChannelItem = BuildRcChannelMenuItem("Канал вибору", selectionChannelMenuItems, channel =>
            {
                selectionChannel = channel;
                UpdateStateFromRcChannels();
            });

            menu.Items.Add(iconsCountItem);
            menu.Items.Add(commandChannelItem);
            menu.Items.Add(safetyChannelItem);
            menu.Items.Add(selectionChannelItem);

            UpdateRcChannelMenuChecks(commandChannelMenuItems, commandChannel);
            UpdateRcChannelMenuChecks(safetyChannelMenuItems, safetyChannel);
            UpdateRcChannelMenuChecks(selectionChannelMenuItems, selectionChannel);

            return menu;
        }

        private ToolStripMenuItem BuildRcChannelMenuItem(string title, List<ToolStripMenuItem> menuItems, Action<int> onSelected)
        {
            var item = new ToolStripMenuItem(title);

            for (var channel = MinRcChannel; channel <= MaxRcChannel; channel++)
            {
                var rcChannel = channel;
                var channelItem = new ToolStripMenuItem($"Канал {rcChannel}")
                {
                    CheckOnClick = true,
                    Tag = rcChannel
                };

                channelItem.Click += (_, __) =>
                {
                    onSelected(rcChannel);
                    UpdateRcChannelMenuChecks(menuItems, rcChannel);
                };

                menuItems.Add(channelItem);
                item.DropDownItems.Add(channelItem);
            }

            return item;
        }

        private void UpdateRcChannelMenuChecks(List<ToolStripMenuItem> menuItems, int selectedChannel)
        {
            foreach (var item in menuItems)
            {
                item.Checked = item.Tag is int channel && channel == selectedChannel;
            }
        }

        private void ApplyVisibleIconsCount()
        {
            var visibleRowsCount = visibleIconsCount + 1;
            UpdateOverlayFormSize(visibleRowsCount);

            safetyIcon.Visible = true;
            iconsLayout.RowStyles[0].SizeType = SizeType.Percent;
            iconsLayout.RowStyles[0].Height = 100f / visibleRowsCount;

            for (var i = 0; i < icons.Count; i++)
            {
                var isVisible = i < visibleIconsCount;
                icons[i].Visible = isVisible;
                iconsLayout.RowStyles[i + 1].SizeType = isVisible ? SizeType.Percent : SizeType.Absolute;
                iconsLayout.RowStyles[i + 1].Height = isVisible ? 100f / visibleRowsCount : 0f;
            }

            for (var i = 0; i < iconCountMenuItems.Count; i++)
            {
                iconCountMenuItems[i].Checked = i + 1 == visibleIconsCount;
            }

            ApplyOverlayState();
        }

        private void UpdateOverlayFormSize(int visibleRowsCount)
        {
            var targetClientHeight = iconsLayout.Padding.Vertical + (visibleRowsCount * iconRowHeight);
            var targetFormHeight = baseNonClientHeight + targetClientHeight;
            Size = new Size(BaseFormWidth, targetFormHeight);
        }


        private void UpdateStateFromRcChannels()
        {
            safetyActive = ReadRcChannelValue(safetyChannel) > SafetyActivationThreshold;
            commandActive = ReadRcChannelValue(commandChannel) > CommandActivationThreshold;

            var selectionChannelValue = ReadRcChannelValue(selectionChannel);
            if (selectionChannelValue != lastSelectionChannelValue)
            {
                log.Info($"Значення каналу вибору CH{selectionChannel} змінено: {selectionChannelValue}");
                lastSelectionChannelValue = selectionChannelValue;
            }

            selectedIconIndex = ResolveSelectedIconIndex(selectionChannelValue);

            blinkTimer.Enabled = safetyActive && commandActive && selectedIconIndex >= 0;
            if (!blinkTimer.Enabled)
            {
                blinkIsRed = false;
            }

            ApplyOverlayState();
        }

        private static int ResolveSelectedIconIndex(int channelValue)
        {
            if (channelValue >= 801 && channelValue <= 1100)
                return 0;
            if (channelValue >= 1101 && channelValue <= 1300)
                return 1;
            if (channelValue >= 1301 && channelValue <= 1500)
                return 2;
            if (channelValue >= 1501 && channelValue <= 1700)
                return 3;
            if (channelValue >= 1701 && channelValue <= 1900)
                return 4;
            if (channelValue >= 1901 && channelValue <= 2200)
                return 5;

            return -1;
        }

        private int ReadRcChannelValue(int channelNumber)
        {
            var index = channelNumber - 1;
            if (index < 0 || index >= rcChannelValues.Length)
            {
                return 0;
            }

            return rcChannelValues[index];
        }

        private void ApplyOverlayState()
        {
            safetyIcon.Image = safetyActive ? safeOnImage : safeOffImage;

            for (var i = 0; i < icons.Count; i++)
            {
                var isLocked = (lockMask & (1 << i)) != 0;
                var isSelectedIcon = safetyActive && selectedIconIndex == i;

                if (isSelectedIcon)
                {
                    var showRed = commandActive && blinkIsRed;
                    icons[i].Image = ResolveSelectedIconImage(isLocked, showRed);
                }
                else
                {
                    icons[i].Image = isLocked ? lockedImage : unlockedImage;
                }
            }
        }

        private Image ResolveSelectedIconImage(bool isLocked, bool showRed)
        {
            if (showRed)
            {
                return isLocked ? lockedRedImage : unlockedRedImage;
            }

            return isLocked ? lockedOrangeImage : unlockedOrangeImage;
        }

        private void RegisterDragEvents(Control control)
        {
            control.MouseDown += OnAnyControlMouseDown;
            control.MouseMove += OnAnyControlMouseMove;
            control.MouseUp += OnAnyControlMouseUp;

            foreach (Control childControl in control.Controls)
            {
                RegisterDragEvents(childControl);
            }
        }

        private void OnAnyControlMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var sourceControl = sender as Control;
            if (sourceControl == null)
            {
                return;
            }

            isDragging = true;
            var mouseScreenPosition = sourceControl.PointToScreen(e.Location);
            dragOffset = new Point(mouseScreenPosition.X - Left, mouseScreenPosition.Y - Top);
        }

        private void OnAnyControlMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }

            var sourceControl = sender as Control;
            if (sourceControl == null)
            {
                return;
            }

            var mouseScreenPosition = sourceControl.PointToScreen(e.Location);
            Left = mouseScreenPosition.X - dragOffset.X;
            Top = mouseScreenPosition.Y - dragOffset.Y;
        }

        private void OnAnyControlMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                SaveCurrentPosition();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveCurrentPosition();
            base.OnFormClosing(e);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                unlockedImage?.Dispose();
                lockedImage?.Dispose();
                unlockedOrangeImage?.Dispose();
                lockedOrangeImage?.Dispose();
                unlockedRedImage?.Dispose();
                lockedRedImage?.Dispose();
                safeOnImage?.Dispose();
                safeOffImage?.Dispose();
                blinkTimer?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string ResolveIconImagePath(string fileName)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new[]
            {
                Path.Combine(baseDirectory, "Controls", "Icon", fileName),
                Path.Combine(baseDirectory, "..", "..", "..", "Controls", "Icon", fileName),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "Controls", "Icon", fileName)
            };

            foreach (var candidatePath in candidatePaths)
            {
                var fullPath = Path.GetFullPath(candidatePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException($"Не вдалося знайти файл іконки {fileName}.");
        }

        private void LoadSavedPosition()
        {
            var settings = Utilities.Settings.Instance;
            if (!settings.ContainsKey(PositionXSettingKey) || !settings.ContainsKey(PositionYSettingKey))
            {
                return;
            }

            var savedX = settings.GetInt32(PositionXSettingKey, Left);
            var savedY = settings.GetInt32(PositionYSettingKey, Top);
            var savedBounds = new Rectangle(savedX, savedY, Width, Height);

            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(savedBounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(savedX, savedY);
                    return;
                }
            }
        }

        private void SaveCurrentPosition()
        {
            var settings = Utilities.Settings.Instance;
            settings[PositionXSettingKey] = Left.ToString();
            settings[PositionYSettingKey] = Top.ToString();
        }
    }
}