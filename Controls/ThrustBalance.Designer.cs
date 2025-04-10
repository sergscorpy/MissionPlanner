using DirectShowLib;
using MissionPlanner.GCSViews;
using System;
using System.Linq;

namespace MissionPlanner.Controls
{
    partial class ThrustBalance
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.servo1_lbl = new System.Windows.Forms.Label();
            this.servo2_lbl = new System.Windows.Forms.Label();
            this.servo3_lbl = new System.Windows.Forms.Label();
            this.servo4_lbl = new System.Windows.Forms.Label();
            this.update_timer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // servo1_lbl
            // 
            this.servo1_lbl.AutoSize = true;
            this.servo1_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo1_lbl.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo1_lbl.ForeColor = System.Drawing.Color.ForestGreen;
            this.servo1_lbl.Location = new System.Drawing.Point(15, 15);
            this.servo1_lbl.Name = "servo1_lbl";
            this.servo1_lbl.Size = new System.Drawing.Size(77, 40);
            this.servo1_lbl.TabIndex = 2;
            this.servo1_lbl.Text = "10₁";
            this.servo1_lbl.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // servo2_lbl
            // 
            this.servo2_lbl.AutoSize = true;
            this.servo2_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo2_lbl.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo2_lbl.ForeColor = System.Drawing.Color.ForestGreen;
            this.servo2_lbl.Location = new System.Drawing.Point(170, 15);
            this.servo2_lbl.Name = "servo2_lbl";
            this.servo2_lbl.Size = new System.Drawing.Size(77, 40);
            this.servo2_lbl.TabIndex = 3;
            this.servo2_lbl.Text = "₂10";
            // 
            // servo3_lbl
            // 
            this.servo3_lbl.AutoSize = true;
            this.servo3_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo3_lbl.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo3_lbl.ForeColor = System.Drawing.Color.ForestGreen;
            this.servo3_lbl.Location = new System.Drawing.Point(170, 170);
            this.servo3_lbl.Name = "servo3_lbl";
            this.servo3_lbl.Size = new System.Drawing.Size(77, 40);
            this.servo3_lbl.TabIndex = 5;
            this.servo3_lbl.Text = "³10";
            // 
            // servo4_lbl
            // 
            this.servo4_lbl.AutoSize = true;
            this.servo4_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo4_lbl.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo4_lbl.ForeColor = System.Drawing.Color.ForestGreen;
            this.servo4_lbl.Location = new System.Drawing.Point(15, 170);
            this.servo4_lbl.Name = "servo4_lbl";
            this.servo4_lbl.Size = new System.Drawing.Size(77, 40);
            this.servo4_lbl.TabIndex = 4;
            this.servo4_lbl.Text = "10⁴";
            this.servo4_lbl.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // ThrustBalance
            // 
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowOnly;
            this.BackColor = System.Drawing.SystemColors.ControlText;
            this.BackgroundImage = global::MissionPlanner.Properties.Resources.quadframesnormal_03;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.ClientSize = new System.Drawing.Size(240, 220);
            this.Controls.Add(this.servo3_lbl);
            this.Controls.Add(this.servo4_lbl);
            this.Controls.Add(this.servo2_lbl);
            this.Controls.Add(this.servo1_lbl);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ThrustBalance";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Thrust Balance";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();
            // 
            // update_timer
            // 
            this.update_timer.Tick += new System.EventHandler(this.update_timer_Tick);

        }

        #endregion
        private System.Windows.Forms.Label servo1_lbl;
        private System.Windows.Forms.Label servo2_lbl;
        private System.Windows.Forms.Label servo4_lbl;
        private System.Windows.Forms.Label servo3_lbl;
        private System.Windows.Forms.Timer update_timer;
        private FlightData.motorBalanceChecker motorBalanceChecker;
        private System.Drawing.Color[] colorGradient;
    }
}