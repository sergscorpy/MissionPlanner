using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public class ServoControllerModuleOverlayForm : Form
    {
        private const int MaxIconsCount = 8;
        private const int DefaultVisibleIconsCount = 4;

        private readonly TableLayoutPanel iconsLayout;
        private readonly List<PictureBox> icons = new List<PictureBox>();
        private readonly List<ToolStripMenuItem> iconCountMenuItems = new List<ToolStripMenuItem>();
        private readonly Image unlockedImage;
        private readonly Image lockedImage;

        private bool isDragging;
        private Point dragOffset;
        private int visibleIconsCount = DefaultVisibleIconsCount;
        private int lockMask;

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

            iconsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = MaxIconsCount,
                Padding = new Padding(20, 15, 20, 15)
            };

            for (var i = 0; i < MaxIconsCount; i++)
            {
                iconsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / MaxIconsCount));
            }

            unlockedImage = Image.FromFile(ResolveIconImagePath("Drops_Empty.png"));
            lockedImage = Image.FromFile(ResolveIconImagePath("Drops_Green.png"));

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
                iconsLayout.Controls.Add(icon, 0, i);
            }

            Controls.Add(iconsLayout);

            ContextMenuStrip = BuildIconsCountMenu();
            RegisterDragEvents(this);
            ApplyVisibleIconsCount();
            ApplyLockMask();
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
                BeginInvoke((Action)ApplyLockMask);
                return;
            }

            ApplyLockMask();
        }

        private ContextMenuStrip BuildIconsCountMenu()
        {
            var menu = new ContextMenuStrip();
            var titleItem = new ToolStripMenuItem("Кількість іконок")
            {
                Enabled = false
            };

            menu.Items.Add(titleItem);
            menu.Items.Add(new ToolStripSeparator());

            for (var i = 1; i <= MaxIconsCount; i++)
            {
                var count = i;
                var item = new ToolStripMenuItem(count.ToString())
                {
                    CheckOnClick = true
                };

                item.Click += (_, __) =>
                {
                    visibleIconsCount = count;
                    ApplyVisibleIconsCount();
                };

                iconCountMenuItems.Add(item);
                menu.Items.Add(item);
            }

            return menu;
        }

        private void ApplyVisibleIconsCount()
        {
            for (var i = 0; i < icons.Count; i++)
            {
                var isVisible = i < visibleIconsCount;
                icons[i].Visible = isVisible;
                iconsLayout.RowStyles[i].SizeType = isVisible ? SizeType.Percent : SizeType.Absolute;
                iconsLayout.RowStyles[i].Height = isVisible ? 100f / visibleIconsCount : 0f;
            }

            for (var i = 0; i < iconCountMenuItems.Count; i++)
            {
                iconCountMenuItems[i].Checked = i + 1 == visibleIconsCount;
            }

            ApplyLockMask();
        }

        private void ApplyLockMask()
        {
            for (var i = 0; i < icons.Count; i++)
            {
                var isLocked = (lockMask & (1 << i)) != 0;
                icons[i].Image = isLocked ? lockedImage : unlockedImage;
            }
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
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                unlockedImage?.Dispose();
                lockedImage?.Dispose();
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
    }
}