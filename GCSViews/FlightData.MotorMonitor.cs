using System;
using System.Windows.Forms;
using MissionPlanner.Controls;
using MissionPlanner.Controls.MotorMonitor;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private MotorMonitorForm motorMonitor;

        private void InitializeMotorMonitorMenu()
        {
            var item = new ToolStripMenuItem("Монітор двигунів…");
            item.Click += OpenMotorMonitor;
            contextMenuStripHud.Items.Add(new ToolStripSeparator());
            contextMenuStripHud.Items.Add(item);
            Disposed += (s, e) => motorMonitor?.Close();
        }

        private void OpenMotorMonitor(object sender, EventArgs e)
        {
            if (motorMonitor != null && !motorMonitor.IsDisposed)
            {
                if (motorMonitor.WindowState == FormWindowState.Minimized)
                    motorMonitor.WindowState = FormWindowState.Normal;
                motorMonitor.Activate();
                return;
            }
            try
            {
                motorMonitor = new MotorMonitorForm();
                motorMonitor.FormClosed += (s, args) => motorMonitor = null;
                motorMonitor.Show(FindForm());
            }
            catch (Exception ex)
            {
                motorMonitor?.Dispose();
                motorMonitor = null;
                CustomMessageBox.Show("Не вдалося відкрити монітор двигунів: " + ex.Message, "Монітор двигунів");
            }
        }
    }
}
