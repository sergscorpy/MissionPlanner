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
        private bool suppressEvents;

        public RcLinkView()
        {
            Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(20),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

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
                Text = "Пересканувати порти",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            rescanButton.Click += RescanButton_Click;
            layout.Controls.Add(rescanButton, 1, 3);

            Controls.Add(layout);

            LoadProfiles();
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
    }
}