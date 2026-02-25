using System;
using System.Drawing;
using System.IO;
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
            Size = new Size(150, 360);

            var iconsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(20, 15, 20, 15)
            };

            for (var i = 0; i < 4; i++)
            {
                iconsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            }

            var iconPath = ResolveDropsEmptyImagePath();
            for (var i = 0; i < 4; i++)
            {
                var icon = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = Image.FromFile(iconPath),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(0, 4, 0, 4)
                };

                iconsLayout.Controls.Add(icon, 0, i);
            }

            Controls.Add(iconsLayout);
        }

        private static string ResolveDropsEmptyImagePath()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new[]
            {
                Path.Combine(baseDirectory, "Controls", "Icon", "Drops_Empty.png"),
                Path.Combine(baseDirectory, "..", "..", "..", "Controls", "Icon", "Drops_Empty.png"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "Controls", "Icon", "Drops_Empty.png")
            };

            foreach (var candidatePath in candidatePaths)
            {
                var fullPath = Path.GetFullPath(candidatePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException("Не вдалося знайти файл іконки Drops_Empty.png.");
        }
    }
}