using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public class ServoControllerModuleOverlayForm : Form
    {
        public ServoControllerModuleOverlayForm()
        {
            Text = "Плата контролю";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(320, 140);

            var label = new Label
            {
                Text = "Плата контролю",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold)
            };

            Controls.Add(label);
        }
    }
}