using MissionPlanner.GCSViews;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public partial class ThrustBalance : Form
    {
        public ThrustBalance()
        {
            InitializeComponent();

            Utilities.ThemeManager.ApplyThemeTo(this);

            update_timer_Tick(null, null);
            update_timer.Start();
        }

        private void update_timer_Tick(object sender, EventArgs e)
        {
            int servo1_pct = (int)(motorBalanceChecker.motor1_pct * 100);
            int servo2_pct = (int)(motorBalanceChecker.motor2_pct * 100);
            int servo3_pct = (int)(motorBalanceChecker.motor3_pct * 100);
            int servo4_pct = (int)(motorBalanceChecker.motor4_pct * 100);

            motorBalanceChecker.update();
            this.servo1_lbl.Text = $"{servo1_pct}₁";
            this.servo2_lbl.Text = $"₂{servo2_pct}";
            this.servo3_lbl.Text = $"³{servo3_pct}";
            this.servo4_lbl.Text = $"{servo4_pct}⁴";

            this.servo1_lbl.ForeColor = colorGradient[servo1_pct];
            this.servo2_lbl.ForeColor = colorGradient[servo2_pct];
            this.servo3_lbl.ForeColor = colorGradient[servo3_pct];
            this.servo4_lbl.ForeColor = colorGradient[servo4_pct];
        }
        private static IEnumerable<Color> GetColorGradient(Color from, Color to, int totalNumberOfColors)
        {
            if (totalNumberOfColors < 2)
            {
                throw new ArgumentException("Gradient cannot have less than two colors.", nameof(totalNumberOfColors));
            }

            double diffA = to.A - from.A;
            double diffR = to.R - from.R;
            double diffG = to.G - from.G;
            double diffB = to.B - from.B;

            var steps = totalNumberOfColors - 1;

            var stepA = diffA / steps;
            var stepR = diffR / steps;
            var stepG = diffG / steps;
            var stepB = diffB / steps;

            yield return from;

            for (var i = 1; i < steps; ++i)
            {
                yield return Color.FromArgb(
                    c(from.A, stepA),
                    c(from.R, stepR),
                    c(from.G, stepG),
                    c(from.B, stepB));

                int c(int fromC, double stepC)
                {
                    return (int)Math.Round(fromC + stepC * i);
                }
            }

            yield return to;
        }
    }
}
