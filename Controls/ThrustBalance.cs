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

            colorGradient =
                GetColorBand(
                    100,
                    new[] { System.Drawing.Color.LimeGreen, System.Drawing.Color.Yellow, System.Drawing.Color.Red },
                    new[] { 50, 80 }
                ).ToArray();
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
            this.servo1_pct.ForeColor = getForeColor(servo1_pct, colorGradient);
            this.servo2_pct.ForeColor = getForeColor(servo2_pct, colorGradient);
            this.servo3_pct.ForeColor = getForeColor(servo3_pct, colorGradient);
            this.servo4_pct.ForeColor = getForeColor(servo4_pct, colorGradient);

            this.servo1_pct.Text = $"{servo1_pct}%";
            this.servo2_pct.Text = $"{servo2_pct}%";
            this.servo3_pct.Text = $"{servo3_pct}%";
            this.servo4_pct.Text = $"{servo4_pct}%";
        }

        private Color getForeColor(int motor_pct, Color[] colorBand)
        {
            int max_index = colorBand.Length - 1;
            bool out_of_bounds = motor_pct < 0 || motor_pct > max_index;
            return colorBand[out_of_bounds ? max_index : motor_pct];
        }
        
        private static IEnumerable<Color> GetColorBand(int size, Color[] color, int[] band = null)
        {
            
            if (band is null)
            {
                band = Array.Empty<int>();
            };
            int bandDiff = color.Length - band.Length;
            if (bandDiff > 0)
            {
                int lastBand = band.Length > 0 ? band[band.Length - 1] : 0;
                for (int i = 1; i < bandDiff + 1; i++)
                {
                    band = band.Append(lastBand + i * (size - lastBand) / bandDiff).ToArray();
                };
            };
            
            var total = color.Zip(band, (c, b) => new { Color = c, Band = b });
            int cur_size = 0;
            foreach (var cb in total)
            {
                for (int i = 0; i < cb.Band - cur_size; i++)
                {
                    yield return cb.Color;
                }
                cur_size = cb.Band;
            };
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
