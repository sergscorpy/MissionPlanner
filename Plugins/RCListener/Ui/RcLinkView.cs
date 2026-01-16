using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Controls;
using RCListener.Camera;

namespace RCListener.Ui
{
    public class RcLinkView : MyUserControl, IActivate
    {
        private readonly ComboBox cameraCombo;
        private readonly Label cameraDetails;
        private readonly Label cameraLabel;
        private readonly Button rescanButton;
        private readonly Label gripperStatusLabel;
        private readonly CheckBox gripperEnabledCheck;
        private readonly NumericUpDown gripperServoChannel;
        private readonly NumericUpDown gripperTriggerChannel;
        private readonly NumericUpDown gripperServoCount;
        private readonly Panel[] lockBorders = new Panel[4];
        private readonly Panel[] lockPanels = new Panel[4];
        private bool suppressEvents;

        public RcLinkView()
        {
            Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 8,
                RowCount = 5,
                Padding = new Padding(20),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var PercentN = 100 / (layout.ColumnCount - 2);
            for (int i = 0; i < (layout.ColumnCount - 2); i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, PercentN));
            }

            var title = new Label
            {
                Text = "RC LINK",
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 2);

            cameraLabel = new Label
            {
                Text = "Камера:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(cameraLabel, 0, 1);

            cameraCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cameraCombo.SelectedIndexChanged += CameraCombo_SelectedIndexChanged;
            layout.Controls.Add(cameraCombo, 1, 1);

            var detailLabel = new Label
            {
                Text = "UDP:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(detailLabel, 0, 2);

            cameraDetails = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(cameraDetails, 1, 2);

            rescanButton = new Button
            {
                Text = "Rescan COM ports",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            rescanButton.Click += RescanButton_Click;
            layout.Controls.Add(rescanButton, 1, 3);

            var gripperGroup = new GroupBox
            {
                Text = "Gripper",
                AutoSize = true,
                Dock = DockStyle.Top
            };

            var gripperLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10)
            };
            gripperLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gripperLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var gripperStatusTitle = new Label
            {
                Text = "Стан пристрою:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(gripperStatusTitle, 0, 0);

            gripperStatusLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(gripperStatusLabel, 1, 0);

            gripperEnabledCheck = new CheckBox
            {
                Text = "Увімкнути керування gripper",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            gripperEnabledCheck.CheckedChanged += GripperEnabledCheck_CheckedChanged;
            gripperLayout.Controls.Add(gripperEnabledCheck, 0, 1);
            gripperLayout.SetColumnSpan(gripperEnabledCheck, 2);

            var servoChannelLabel = new Label
            {
                Text = "Канал вибору сервопривода:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(servoChannelLabel, 0, 2);

            gripperServoChannel = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 24,
                Dock = DockStyle.Left,
                Width = 80
            };
            gripperServoChannel.ValueChanged += GripperServoChannel_ValueChanged;
            gripperLayout.Controls.Add(gripperServoChannel, 1, 2);

            var triggerChannelLabel = new Label
            {
                Text = "Канал тригера:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(triggerChannelLabel, 0, 3);

            gripperTriggerChannel = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 24,
                Dock = DockStyle.Left,
                Width = 80
            };
            gripperTriggerChannel.ValueChanged += GripperTriggerChannel_ValueChanged;
            gripperLayout.Controls.Add(gripperTriggerChannel, 1, 3);

            var servoCountLabel = new Label
            {
                Text = "К-сть сервоприводів:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(servoCountLabel, 0, 4);

            gripperServoCount = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 4,
                Dock = DockStyle.Left,
                Width = 80
            };
            gripperServoCount.ValueChanged += GripperServoCount_ValueChanged;
            gripperLayout.Controls.Add(gripperServoCount, 1, 4);

            var lockStatusLabel = new Label
            {
                Text = "Стан замків:",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gripperLayout.Controls.Add(lockStatusLabel, 0, 5);

            var lockFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            for (int i = 0; i < lockBorders.Length; i++)
            {
                var border = new Panel
                {
                    Width = 26,
                    Height = 26,
                    Padding = new Padding(3),
                    BackColor = SystemColors.Control
                };

                var inner = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Gray
                };

                border.Controls.Add(inner);
                lockBorders[i] = border;
                lockPanels[i] = inner;
                lockFlow.Controls.Add(border);
            }

            gripperLayout.Controls.Add(lockFlow, 1, 5);

            gripperGroup.Controls.Add(gripperLayout);
            layout.Controls.Add(gripperGroup, 0, 4);
            layout.SetColumnSpan(gripperGroup, 3);

            Controls.Add(layout);

            LoadProfiles();
            LoadGripperSettings();
        }

        public void Activate()
        {
            UpdateFromService();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var service = RcListenerContext.CameraSelection;
                if (service != null)
                    service.CameraChanged -= HandleCameraChanged;

                var gripper = RcListenerContext.GripperControl;
                if (gripper != null)
                    gripper.Updated -= HandleGripperUpdated;
            }

            base.Dispose(disposing);
        }

        private void LoadProfiles()
        {
            cameraCombo.Items.Clear();

            var service = RcListenerContext.CameraSelection;
            if (service == null)
            {
                cameraCombo.Enabled = false;
                cameraDetails.Text = "Немає даних.";
                return;
            }

            foreach (var profile in service.Profiles)
            {
                cameraCombo.Items.Add(profile.Name);
            }

            service.CameraChanged += HandleCameraChanged;
            UpdateSelection(service.Current);
        }

        private void UpdateFromService()
        {
            var service = RcListenerContext.CameraSelection;
            if (service == null)
                return;

            UpdateSelection(service.Current);
            UpdateGripperUi();
        }

        private void UpdateSelection(ICameraProfile profile)
        {
            if (profile == null)
                return;

            suppressEvents = true;
            cameraCombo.SelectedItem = profile.Name;
            cameraDetails.Text = $"{profile.UdpIp}:{profile.UdpPort}";
            suppressEvents = false;
        }

        private void CameraCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressEvents)
                return;

            var selected = cameraCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected))
                return;

            var service = RcListenerContext.CameraSelection;
            service?.SetCamera(selected);
        }

        private void HandleCameraChanged(ICameraProfile profile)
        {
            UpdateSelection(profile);
        }

        private void RescanButton_Click(object sender, EventArgs e)
        {
            RcListenerContext.RequestRescan?.Invoke();
        }

        private void LoadGripperSettings()
        {
            var gripper = RcListenerContext.GripperControl;
            if (gripper == null)
                return;

            gripper.Updated += HandleGripperUpdated;
            UpdateGripperUi();
        }

        private void HandleGripperUpdated()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateGripperUi);
                return;
            }

            UpdateGripperUi();
        }

        private void UpdateGripperUi()
        {
            var gripper = RcListenerContext.GripperControl;
            if (gripper == null)
                return;

            suppressEvents = true;

            gripperEnabledCheck.Checked = gripper.IsEnabled;
            gripperEnabledCheck.Enabled = gripper.IsDeviceAvailable;
            gripperStatusLabel.Text = gripper.IsDeviceAvailable ? "Підключено" : "Немає з'єднання";

            gripperServoChannel.Value = gripper.ServoSelectionChannel;
            gripperTriggerChannel.Value = gripper.TriggerChannel;
            gripperServoCount.Value = gripper.ServoCount;

            for (int i = 0; i < lockPanels.Length; i++)
            {
                bool visible = i < gripper.ServoCount;
                lockBorders[i].Visible = visible;
                if (!visible)
                    continue;

                bool locked = gripper.LockStates[i];
                lockPanels[i].BackColor = locked ? Color.LimeGreen : Color.Red;
                lockBorders[i].BackColor = (gripper.SelectedServo == i + 1) ? Color.Gold : SystemColors.Control;
            }

            suppressEvents = false;
        }

        private void GripperEnabledCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressEvents)
                return;

            var gripper = RcListenerContext.GripperControl;
            gripper?.SetEnabled(gripperEnabledCheck.Checked);
        }

        private void GripperServoChannel_ValueChanged(object sender, EventArgs e)
        {
            if (suppressEvents)
                return;

            var gripper = RcListenerContext.GripperControl;
            gripper?.SetServoSelectionChannel((int)gripperServoChannel.Value);
        }

        private void GripperTriggerChannel_ValueChanged(object sender, EventArgs e)
        {
            if (suppressEvents)
                return;

            var gripper = RcListenerContext.GripperControl;
            gripper?.SetTriggerChannel((int)gripperTriggerChannel.Value);
        }

        private void GripperServoCount_ValueChanged(object sender, EventArgs e)
        {
            if (suppressEvents)
                return;

            var gripper = RcListenerContext.GripperControl;
            gripper?.SetServoCount((int)gripperServoCount.Value);
        }
    }
}