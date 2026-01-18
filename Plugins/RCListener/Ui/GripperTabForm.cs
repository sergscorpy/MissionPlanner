using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RCListener.Gripper;
using RCListener.Logging;

namespace RCListener.Ui
{
    public sealed class GripperTabForm : Form
    {
        private const int AnimationIntervalMs = 300;
        private const int AnimationToggles = 3;
        private const int MaxServos = 4;
        private const int DragThreshold = 4;

        private readonly ILogger log;
        private readonly GripperControlService gripper;
        private readonly string imageDir;
        private readonly PictureBox enabledButton;
        private readonly PictureBox[] lockIcons;
        private readonly Timer animationTimer;
        private readonly AnimationState[] animationStates;
        private bool dragPending;
        private bool isDragging;
        private Point dragOrigin;
        private Point formOrigin;

        private Image gripEnabledOn;
        private Image gripEnabledOff;
        private Image dropsEmpty;
        private Image dropsGreen;
        private Image dropsOrange;
        private Image dropsRed;
        private Image dropsEmptyRed;
        private Image dropsEmptyOrange;

        public GripperTabForm(ILogger log, GripperControlService gripper)
        {
            this.log = log;
            this.gripper = gripper;
            imageDir = Path.Combine(
                Path.GetDirectoryName(typeof(GripperTabForm).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory,
                "Ui",
                "Image");

            lockIcons = new PictureBox[MaxServos];
            animationStates = Enumerable.Range(0, MaxServos).Select(_ => new AnimationState()).ToArray();
            animationTimer = new Timer { Interval = AnimationIntervalMs };
            animationTimer.Tick += AnimationTimer_Tick;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            Padding = new Padding(6);

            enabledButton = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            enabledButton.Click += EnabledButton_Click;

            var lockFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };

            for (int i = 0; i < lockIcons.Length; i++)
            {
                var icon = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 4, 0, 4)
                };
                lockIcons[i] = icon;
                lockFlow.Controls.Add(icon);
            }

            var layout = new FlowLayoutPanel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            layout.Controls.Add(enabledButton);
            layout.Controls.Add(lockFlow);

            Controls.Add(layout);
            AttachDragHandlers(this);

            LoadImages();
            UpdateFromService();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void UpdateFromService()
        {
            if (gripper == null)
                return;

            enabledButton.Image = gripper.IsEnabled ? gripEnabledOn : gripEnabledOff;
            enabledButton.Enabled = gripper.IsDeviceAvailable;

            for (int i = 0; i < lockIcons.Length; i++)
            {
                bool visible = i < gripper.ServoCount;
                lockIcons[i].Visible = visible;
                if (!visible)
                    continue;

                if (!animationStates[i].IsActive)
                    lockIcons[i].Image = GetBaseIcon(i);
            }
        }

        public void StartAnimation(int servo, bool isLocked)
        {
            int index = servo - 1;
            if (index < 0 || index >= animationStates.Length)
                return;

            animationStates[index].IsActive = true;
            animationStates[index].IsLocked = isLocked;
            animationStates[index].UsePrimary = true;
            animationStates[index].RemainingToggles = AnimationToggles;
            lockIcons[index].Image = GetAnimatedIcon(animationStates[index]);

            if (!animationTimer.Enabled)
                animationTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PositionForm();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;

            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Tick -= AnimationTimer_Tick;
                enabledButton.Click -= EnabledButton_Click;
                DetachDragHandlers(this);
                DisposeImages();
            }

            base.Dispose(disposing);
        }

        private void EnabledButton_Click(object sender, EventArgs e)
        {
            if (gripper == null)
                return;

            gripper.SetEnabled(!gripper.IsEnabled);
            UpdateFromService();
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            bool anyActive = false;

            for (int i = 0; i < animationStates.Length; i++)
            {
                var state = animationStates[i];
                if (!state.IsActive)
                    continue;

                if (state.RemainingToggles == 0)
                {
                    state.IsActive = false;
                    lockIcons[i].Image = GetBaseIcon(i);
                    continue;
                }

                state.UsePrimary = !state.UsePrimary;
                state.RemainingToggles--;
                lockIcons[i].Image = GetAnimatedIcon(state);
                anyActive = true;
            }

            if (!anyActive)
                animationTimer.Stop();
        }

        private Image GetBaseIcon(int index)
        {
            if (gripper == null)
                return dropsEmpty;

            bool locked = gripper.LockStates[index];
            if (!locked)
            {
                bool selectedOpen = gripper.SelectedServo == index + 1;
                return selectedOpen ? dropsEmptyOrange : dropsEmpty;
            }

            bool selected = gripper.SelectedServo == index + 1;
            return selected ? dropsOrange : dropsGreen;
        }

        private Image GetAnimatedIcon(AnimationState state)
        {
            if (state.IsLocked)
                return state.UsePrimary ? dropsRed : dropsOrange;

            return state.UsePrimary ? dropsEmptyRed : dropsEmptyOrange;
        }

        private void PositionForm()
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Location = new Point(area.Right - Width - 20, area.Top + 20);
        }

        private void AttachDragHandlers(System.Windows.Forms.Control control)
        {
            control.MouseDown += DragMouseDown;
            control.MouseMove += DragMouseMove;
            control.MouseUp += DragMouseUp;

            foreach (System.Windows.Forms.Control child in control.Controls)
                AttachDragHandlers(child);
        }

        private void DetachDragHandlers(System.Windows.Forms.Control control)
        {
            control.MouseDown -= DragMouseDown;
            control.MouseMove -= DragMouseMove;
            control.MouseUp -= DragMouseUp;

            foreach (System.Windows.Forms.Control child in control.Controls)
                DetachDragHandlers(child);
        }

        private void DragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            dragPending = true;
            isDragging = false;
            dragOrigin = System.Windows.Forms.Control.MousePosition;
            formOrigin = Location;
        }

        private void DragMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragPending)
                return;

            var current = System.Windows.Forms.Control.MousePosition;
            if (!isDragging)
            {
                if (Math.Abs(current.X - dragOrigin.X) < DragThreshold
                    && Math.Abs(current.Y - dragOrigin.Y) < DragThreshold)
                    return;

                isDragging = true;
                Capture = true;
            }

            Location = new Point(
                formOrigin.X + (current.X - dragOrigin.X),
                formOrigin.Y + (current.Y - dragOrigin.Y));
        }

        private void DragMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            dragPending = false;
            isDragging = false;
            Capture = false;
        }


        private void LoadImages()
        {
            gripEnabledOn = LoadImage("GripEn_On.png");
            gripEnabledOff = LoadImage("GripEn_Off.png");
            dropsEmpty = LoadImage("Drops_Empty.png");
            dropsGreen = LoadImage("Drops_Green.png");
            dropsOrange = LoadImage("Drops_Orange.png");
            dropsRed = LoadImage("Drops_Red.png");
            dropsEmptyRed = LoadImage("Drops_Empty_Red.png");
            dropsEmptyOrange = LoadImage("Drops_Empty_Orange.png");
        }

        private Image LoadImage(string fileName)
        {
            var path = Path.Combine(imageDir, fileName);
            if (!File.Exists(path))
            {
                log?.Log($"[UI] GripperTab image missing: {path}");
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                {
                    return new Bitmap(img);
                }
            }
            catch (Exception ex)
            {
                log?.Log($"[UI] GripperTab load image failed ({fileName}): {ex.Message}");
                return null;
            }
        }

        private void DisposeImages()
        {
            gripEnabledOn?.Dispose();
            gripEnabledOff?.Dispose();
            dropsEmpty?.Dispose();
            dropsGreen?.Dispose();
            dropsOrange?.Dispose();
            dropsRed?.Dispose();
            dropsEmptyRed?.Dispose();
            dropsEmptyOrange?.Dispose();
        }

        private sealed class AnimationState
        {
            public bool IsActive { get; set; }
            public bool IsLocked { get; set; }
            public bool UsePrimary { get; set; }
            public int RemainingToggles { get; set; }
        }
    }
}