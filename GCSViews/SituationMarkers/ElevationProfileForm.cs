using System.Windows.Forms;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class ElevationProfileForm : Form
    {
        readonly ElevationProfileControl profileControl;

        public ElevationProfileForm(SituationMarkersManager manager)
        {
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
            profileControl.RefreshProfile();
        }
    }
}

