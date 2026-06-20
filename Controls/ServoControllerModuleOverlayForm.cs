using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using log4net;
using MPResources = MissionPlanner.Properties.Resources;

namespace MissionPlanner.Controls
{
    public class ServoControllerModuleOverlayForm : Form
    {
        private sealed class InvertedDomainUpDown : DomainUpDown
        {
            public override void UpButton()
            {
                base.DownButton();
            }

            public override void DownButton()
            {
                base.UpButton();
            }
        }

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
        private const int BaseLayoutHorizontalPadding = 20;
        private const int BaseLayoutVerticalPadding = 15;
        private const int DefaultScalePercent = 100;
        private const int MinScalePercent = 100;
        private const int MaxScalePercent = 200;
        private const int ScaleStepPercent = 10;
        private const int BaseScaleControlsHeight = 34;
        private const string PositionXSettingKey = "ServoControllerModuleOverlayForm.PositionX";
        private const string PositionYSettingKey = "ServoControllerModuleOverlayForm.PositionY";
        private const string ScalePercentSettingKey = "ServoControllerModuleOverlayForm.ScalePercent";
        private const string ProfilesListSettingKey = "ServoControllerModuleOverlayForm.Profiles";
        private const string ActiveProfileSettingKey = "ServoControllerModuleOverlayForm.ActiveProfile";
        private const string ProfileSettingPrefix = "ServoControllerModuleOverlayForm.Profile.";
        private const string DefaultProfileName = "Default";
        private const int DefaultProfileVisibleIconsCount = 2;
        private const int DefaultProfileCommandChannel = 11;
        private const int DefaultProfileSafetyChannel = 12;
        private const int DefaultProfileSelectionChannel = 5;

        private readonly Panel scaleControlsPanel;
        private readonly DomainUpDown scaleDomainUpDown;
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
        private readonly int baseIconRowHeight;
        private ToolStripMenuItem profilesMenuItem;
        private string activeProfileName = DefaultProfileName;
        private int scalePercent = DefaultScalePercent;

        public ServoControllerModuleOverlayForm()
        {
            Text = "Скиди";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(BaseFormWidth, BaseFormHeight);

            scaleDomainUpDown = new InvertedDomainUpDown
            {
                ReadOnly = true,
                Wrap = false,
                TextAlign = HorizontalAlignment.Center,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            for (var scaleValue = MinScalePercent; scaleValue <= MaxScalePercent; scaleValue += ScaleStepPercent)
            {
                scaleDomainUpDown.Items.Add($"{scaleValue}%");
            }

            scaleDomainUpDown.SelectedItemChanged += (_, __) =>
            {
                if (scaleDomainUpDown.SelectedItem == null)
                {
                    return;
                }

                var selectedValueText = scaleDomainUpDown.SelectedItem.ToString()?.TrimEnd('%');
                if (int.TryParse(selectedValueText, out var newScalePercent))
                {
                    SetScale(newScalePercent);
                }
            };

            var scaleLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(6, 4, 6, 4)
            };
            scaleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scaleLayout.Controls.Add(scaleDomainUpDown, 0, 0);

            scaleControlsPanel = new Panel
            {
                Dock = DockStyle.Top
            };
            scaleControlsPanel.Controls.Add(scaleLayout);

            iconsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = MaxIconsCount + 1,
                Padding = new Padding(BaseLayoutHorizontalPadding, BaseLayoutVerticalPadding,
                    BaseLayoutHorizontalPadding, BaseLayoutVerticalPadding)
            };

            for (var i = 0; i < iconsLayout.RowCount; i++)
            {
                iconsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / iconsLayout.RowCount));
            }

            unlockedImage = MPResources.servo_drops_empty;
            lockedImage = MPResources.servo_drops_green;
            unlockedOrangeImage = MPResources.servo_drops_empty_orange;
            lockedOrangeImage = MPResources.servo_drops_orange;
            unlockedRedImage = MPResources.servo_drops_empty_red;
            lockedRedImage = MPResources.servo_drops_red;
            safeOnImage = MPResources.servo_safe_on;
            safeOffImage = MPResources.servo_safe_off;

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
            Controls.Add(scaleControlsPanel);

            baseNonClientHeight = Height - ClientSize.Height;
            var availableContentHeight = Math.Max(1, ClientSize.Height - iconsLayout.Padding.Vertical);
            baseIconRowHeight = Math.Max(1, availableContentHeight / (DefaultVisibleIconsCount + 1));

            LoadScaleSetting();
            SetScaleDomainSelection(scalePercent);

            blinkTimer = new Timer { Interval = 500 };
            blinkTimer.Tick += (_, __) =>
            {
                blinkIsRed = !blinkIsRed;
                ApplyOverlayState();
            };

            EnsureDefaultProfileExists();
            LoadAndActivateProfile(GetInitialProfileName());

            ContextMenuStrip = BuildContextMenu();
            RegisterDragEvents(this);
            LoadSavedPosition();
            ApplyScale();
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
                BeginInvoke((Action)(() =>
                {
                    EnsureVisibleIconsCountCoversLockedIcons();
                    ApplyOverlayState();
                }));
                return;
            }

            EnsureVisibleIconsCountCoversLockedIcons();
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

            var iconsCountItem = new ToolStripMenuItem("Кількість скидів");
            for (var i = 1; i <= MaxIconsCount; i++)
            {
                var count = i;
                var item = new ToolStripMenuItem($"Скид {count}")
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


            profilesMenuItem = new ToolStripMenuItem("Профілі");
            profilesMenuItem.DropDownOpening += (_, __) => RebuildProfilesMenu();

            menu.Items.Add(iconsCountItem);
            menu.Items.Add(commandChannelItem);
            menu.Items.Add(safetyChannelItem);
            menu.Items.Add(selectionChannelItem);
            menu.Items.Add(profilesMenuItem);

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
            var scaledIconRowHeight = GetScaledSize(baseIconRowHeight);
            var targetIconsClientHeight = iconsLayout.Padding.Vertical + (visibleRowsCount * scaledIconRowHeight);
            var targetScaleControlsHeight = GetScaledSize(BaseScaleControlsHeight);
            var targetFormHeight = baseNonClientHeight + targetIconsClientHeight + targetScaleControlsHeight;
            Size = new Size(GetScaledSize(BaseFormWidth), targetFormHeight);
        }

        private void SetScale(int newScalePercent)
        {
            var clampedScalePercent = NormalizeScalePercentToStep(newScalePercent);
            if (clampedScalePercent == scalePercent)
            {
                return;
            }

            scalePercent = clampedScalePercent;
            SaveScaleSetting();
            ApplyScale();
            ApplyVisibleIconsCount();
        }

        private void ApplyScale()
        {
            var scaledHorizontalPadding = GetScaledSize(BaseLayoutHorizontalPadding);
            var scaledVerticalPadding = GetScaledSize(BaseLayoutVerticalPadding);
            iconsLayout.Padding = new Padding(scaledHorizontalPadding, scaledVerticalPadding,
                scaledHorizontalPadding, scaledVerticalPadding);

            scaleControlsPanel.Height = GetScaledSize(BaseScaleControlsHeight);
            SetScaleDomainSelection(scalePercent);
        }

        private static int NormalizeScalePercentToStep(int value)
        {
            var clampedValue = ClampValue(value, MinScalePercent, MaxScalePercent);
            var normalizedStep = (int)Math.Round((clampedValue - MinScalePercent) / (double)ScaleStepPercent);
            return MinScalePercent + (normalizedStep * ScaleStepPercent);
        }

        private void SetScaleDomainSelection(int value)
        {
            var scaleItemText = $"{value}%";
            if (!Equals(scaleDomainUpDown.SelectedItem, scaleItemText))
            {
                scaleDomainUpDown.SelectedItem = scaleItemText;
            }

            scaleDomainUpDown.Text = scaleItemText;
        }

        private int GetScaledSize(int baseValue)
        {
            return Math.Max(1, (int)Math.Round(baseValue * (scalePercent / 100.0)));
        }

        private void LoadScaleSetting()
        {
            var savedScalePercent = Utilities.Settings.Instance.GetInt32(ScalePercentSettingKey, DefaultScalePercent);
            scalePercent = NormalizeScalePercentToStep(savedScalePercent);
        }

        private void SaveScaleSetting()
        {
            Utilities.Settings.Instance[ScalePercentSettingKey] = scalePercent.ToString();
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

        private void EnsureVisibleIconsCountCoversLockedIcons()
        {
            var requiredVisibleIconsCount = GetRequiredVisibleIconsCountFromLockMask();
            if (requiredVisibleIconsCount <= visibleIconsCount)
            {
                return;
            }

            visibleIconsCount = requiredVisibleIconsCount;
            ApplyVisibleIconsCount();
        }

        private int GetRequiredVisibleIconsCountFromLockMask()
        {
            for (var i = MaxIconsCount - 1; i >= 0; i--)
            {
                var isLocked = (lockMask & (1 << i)) != 0;
                if (isLocked)
                {
                    return i + 1;
                }
            }

            return 0;
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

        private void SaveProfileFromDialog()
        {
            var profileName = string.Empty;
            if (InputBox.Show("Збереження профілю", "Введіть назву профілю:", ref profileName) != DialogResult.OK)
            {
                return;
            }

            profileName = profileName.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                MessageBox.Show("Назва профілю не може бути порожньою.", "Профілі", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var profileNames = GetProfileNames();
            var profileExists = profileNames.Any(name => string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase));
            if (profileExists)
            {
                var overwriteResult = MessageBox.Show($"Профіль '{profileName}' вже існує. Перезаписати?", "Профілі",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (overwriteResult != DialogResult.Yes)
                {
                    return;
                }

                profileNames.RemoveAll(name => string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase));
            }

            profileNames.Add(profileName);
            SaveProfile(profileName);
            SaveProfileNames(profileNames);
            SetActiveProfileName(profileName);
        }

        private void RebuildProfilesMenu()
        {
            profilesMenuItem.DropDownItems.Clear();

            var currentActiveProfileName = string.IsNullOrWhiteSpace(activeProfileName) ? DefaultProfileName : activeProfileName;
            var activeProfileItem = new ToolStripMenuItem(currentActiveProfileName);
            var saveActiveProfileItem = new ToolStripMenuItem("Зберегти");
            saveActiveProfileItem.Click += (_, __) => SaveProfile(currentActiveProfileName);
            activeProfileItem.DropDownItems.Add(saveActiveProfileItem);
            profilesMenuItem.DropDownItems.Add(activeProfileItem);

            profilesMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var profileNames = GetProfileNames();
            var availableProfileNames = profileNames
                .Where(name => !string.Equals(name, currentActiveProfileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (availableProfileNames.Count == 0)
            {
                var emptyItem = new ToolStripMenuItem("(пусто)") { Enabled = false };
                profilesMenuItem.DropDownItems.Add(emptyItem);
            }
            else
            {
                foreach (var profileName in availableProfileNames)
                {
                    var profileItem = new ToolStripMenuItem(profileName);
                    var loadItem = new ToolStripMenuItem("Завантажити");
                    loadItem.Click += (_, __) => LoadAndActivateProfile(profileName);

                    var deleteItem = new ToolStripMenuItem("Видалити");
                    deleteItem.Click += (_, __) => DeleteProfile(profileName);

                    profileItem.DropDownItems.Add(loadItem);
                    profileItem.DropDownItems.Add(deleteItem);
                    profilesMenuItem.DropDownItems.Add(profileItem);
                }
            }

            profilesMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var newProfileMenuItem = new ToolStripMenuItem("Новий..");
            newProfileMenuItem.Click += (_, __) => SaveProfileFromDialog();
            profilesMenuItem.DropDownItems.Add(newProfileMenuItem);
        }

        private void LoadAndActivateProfile(string profileName)
        {
            var resolvedProfileName = ResolveExistingProfileName(profileName);
            LoadProfile(resolvedProfileName);
            SetActiveProfileName(resolvedProfileName);
        }

        private static string GetInitialProfileName()
        {
            var savedProfileName = Utilities.Settings.Instance.GetString(ActiveProfileSettingKey, DefaultProfileName).Trim();
            return string.IsNullOrWhiteSpace(savedProfileName) ? DefaultProfileName : savedProfileName;
        }

        private static string ResolveExistingProfileName(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return DefaultProfileName;
            }

            var profileNames = GetProfileNames();
            var existingProfileName = profileNames.FirstOrDefault(name =>
                string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(existingProfileName) ? DefaultProfileName : existingProfileName;
        }

        private void SetActiveProfileName(string profileName)
        {
            activeProfileName = profileName;
            Utilities.Settings.Instance[ActiveProfileSettingKey] = profileName;
        }

        private void DeleteProfile(string profileName)
        {
            var deleteResult = MessageBox.Show($"Видалити профіль '{profileName}'?", "Профілі",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (deleteResult != DialogResult.Yes)
            {
                return;
            }

            var profileNames = GetProfileNames();
            profileNames.RemoveAll(name => string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase));
            SaveProfileNames(profileNames);
            RemoveProfileSettings(profileName);

            if (string.Equals(activeProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            {
                EnsureDefaultProfileExists();
                LoadAndActivateProfile(DefaultProfileName);
            }
        }

        private static List<string> GetProfileNames()
        {
            return Utilities.Settings.Instance.GetList(ProfilesListSettingKey)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SaveProfileNames(List<string> profileNames)
        {
            var settings = Utilities.Settings.Instance;
            if (profileNames == null || profileNames.Count == 0)
            {
                settings.Remove(ProfilesListSettingKey);
                return;
            }

            settings.SetList(ProfilesListSettingKey, profileNames);
        }

        private void SaveProfile(string profileName)
        {
            var settings = Utilities.Settings.Instance;
            var profileKeyPrefix = GetProfileSettingKeyPrefix(profileName);

            settings[$"{profileKeyPrefix}VisibleIconsCount"] = visibleIconsCount.ToString();
            settings[$"{profileKeyPrefix}CommandChannel"] = commandChannel.ToString();
            settings[$"{profileKeyPrefix}SafetyChannel"] = safetyChannel.ToString();
            settings[$"{profileKeyPrefix}SelectionChannel"] = selectionChannel.ToString();
        }

        private void LoadProfile(string profileName)
        {
            var settings = Utilities.Settings.Instance;
            var profileKeyPrefix = GetProfileSettingKeyPrefix(profileName);

            var loadedVisibleIconsCount = ClampValue(
                settings.GetInt32($"{profileKeyPrefix}VisibleIconsCount", visibleIconsCount),
                1,
                MaxIconsCount);

            var loadedCommandChannel = ClampValue(
                settings.GetInt32($"{profileKeyPrefix}CommandChannel", commandChannel),
                MinRcChannel,
                MaxRcChannel);

            var loadedSafetyChannel = ClampValue(
                settings.GetInt32($"{profileKeyPrefix}SafetyChannel", safetyChannel),
                MinRcChannel,
                MaxRcChannel);

            var loadedSelectionChannel = ClampValue(
                settings.GetInt32($"{profileKeyPrefix}SelectionChannel", selectionChannel),
                MinRcChannel,
                MaxRcChannel);

            visibleIconsCount = loadedVisibleIconsCount;
            commandChannel = loadedCommandChannel;
            safetyChannel = loadedSafetyChannel;
            selectionChannel = loadedSelectionChannel;

            UpdateRcChannelMenuChecks(commandChannelMenuItems, commandChannel);
            UpdateRcChannelMenuChecks(safetyChannelMenuItems, safetyChannel);
            UpdateRcChannelMenuChecks(selectionChannelMenuItems, selectionChannel);

            ApplyVisibleIconsCount();
            UpdateStateFromRcChannels();
        }

        private static int ClampValue(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        private static string GetProfileSettingKeyPrefix(string profileName)
        {
            var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(profileName))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return $"{ProfileSettingPrefix}{encodedName}.";
        }

        private static void RemoveProfileSettings(string profileName)
        {
            var settings = Utilities.Settings.Instance;
            var profileKeyPrefix = GetProfileSettingKeyPrefix(profileName);

            settings.Remove($"{profileKeyPrefix}VisibleIconsCount");
            settings.Remove($"{profileKeyPrefix}CommandChannel");
            settings.Remove($"{profileKeyPrefix}SafetyChannel");
            settings.Remove($"{profileKeyPrefix}SelectionChannel");
        }

        private static void EnsureDefaultProfileExists()
        {
            var profileNames = GetProfileNames();
            if (profileNames.Any(name => string.Equals(name, DefaultProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            profileNames.Add(DefaultProfileName);
            SaveProfileNames(profileNames);

            var settings = Utilities.Settings.Instance;
            var profileKeyPrefix = GetProfileSettingKeyPrefix(DefaultProfileName);
            settings[$"{profileKeyPrefix}VisibleIconsCount"] = DefaultProfileVisibleIconsCount.ToString();
            settings[$"{profileKeyPrefix}CommandChannel"] = DefaultProfileCommandChannel.ToString();
            settings[$"{profileKeyPrefix}SafetyChannel"] = DefaultProfileSafetyChannel.ToString();
            settings[$"{profileKeyPrefix}SelectionChannel"] = DefaultProfileSelectionChannel.ToString();
        }
    }
}
