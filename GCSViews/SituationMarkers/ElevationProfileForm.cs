using System.Windows.Forms;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class ElevationProfileForm : Form
    {
        readonly SituationMarkersManager manager;
        readonly ElevationProfileControl profileControl;

        public ElevationProfileForm(SituationMarkersManager manager)
        {
            this.manager = manager;
            this.manager.SelectedMarkerChanged += Manager_SelectedMarkerChanged;
            Text = "Elevation Profile";
            Width = 900;
            Height = 420;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            profileControl = new ElevationProfileControl(manager)
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(profileControl);
        }

        public void RefreshProfile()
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke((MethodInvoker)RefreshProfile);
                return;
            }

            profileControl.RefreshProfile();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            manager.SelectedMarkerChanged -= Manager_SelectedMarkerChanged;
            base.OnFormClosed(e);
        }

        void Manager_SelectedMarkerChanged(object sender, System.EventArgs e)
        {
            RefreshProfile();
        }
    }
}

