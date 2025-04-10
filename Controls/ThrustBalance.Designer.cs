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
            this.servo1_pct = new System.Windows.Forms.Label();
            this.servo1_lbl = new System.Windows.Forms.Label();
            this.servo2_pct = new System.Windows.Forms.Label();
            this.servo2_lbl = new System.Windows.Forms.Label();
            this.servo3_pct = new System.Windows.Forms.Label();
            this.servo3_lbl = new System.Windows.Forms.Label();
            this.servo4_pct = new System.Windows.Forms.Label();
            this.servo4_lbl = new System.Windows.Forms.Label();
            this.update_timer = new System.Windows.Forms.Timer(this.components);
            this.motorBalanceChecker = new FlightData.motorBalanceChecker();
            this.SuspendLayout();
            // 
            // servo1_pct
            // 
            this.servo1_pct.BackColor = System.Drawing.Color.Transparent;
            this.servo1_pct.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo1_pct.ForeColor = System.Drawing.Color.Green;
            this.servo1_pct.Location = new System.Drawing.Point(170, 15);
            this.servo1_pct.Name = "servo1_pct";
            this.servo1_pct.Size = new System.Drawing.Size(77, 40);
            this.servo1_pct.TabIndex = 2;
            this.servo1_pct.Text = "0%";
            this.servo1_pct.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            // 
            // servo1_lbl
            // 
            this.servo1_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo1_lbl.Font = new System.Drawing.Font("Tahoma", 17F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo1_lbl.ForeColor = System.Drawing.Color.White;
            this.servo1_lbl.Location = new System.Drawing.Point(187, 49);
            this.servo1_lbl.Name = "servo1_lbl";
            this.servo1_lbl.Size = new System.Drawing.Size(30, 40);
            this.servo1_lbl.Text = "1";
            this.servo1_lbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // servo2_pct
            // 
            this.servo2_pct.AutoSize = true;
            this.servo2_pct.BackColor = System.Drawing.Color.Transparent;
            this.servo2_pct.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo2_pct.ForeColor = System.Drawing.Color.Green;
            this.servo2_pct.Location = new System.Drawing.Point(170, 150);
            this.servo2_pct.Name = "servo2_lbl";
            this.servo2_pct.Size = new System.Drawing.Size(77, 40);
            this.servo2_pct.TabIndex = 3;
            this.servo2_pct.Text = "0%";
            this.servo2_pct.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            // 
            // servo2_lbl
            // 
            this.servo2_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo2_lbl.Font = new System.Drawing.Font("Tahoma", 17F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo2_lbl.ForeColor = System.Drawing.Color.White;
            this.servo2_lbl.Location = new System.Drawing.Point(187, 117);
            this.servo2_lbl.Name = "servo2_lbl";
            this.servo2_lbl.Size = new System.Drawing.Size(30, 40);
            this.servo2_lbl.Text = "2";
            this.servo2_lbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // servo3_pct
            // 
            this.servo3_pct.AutoSize = true;
            this.servo3_pct.BackColor = System.Drawing.Color.Transparent;
            this.servo3_pct.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo3_pct.ForeColor = System.Drawing.Color.Green;
            this.servo3_pct.Location = new System.Drawing.Point(15, 150);
            this.servo3_pct.Name = "servo3_lbl";
            this.servo3_pct.Size = new System.Drawing.Size(77, 40);
            this.servo3_pct.TabIndex = 5;
            this.servo3_pct.Text = "0%";
            this.servo3_pct.TextAlign = System.Drawing.ContentAlignment.BottomRight;
            // 
            // servo3_lbl
            // 
            this.servo3_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo3_lbl.Font = new System.Drawing.Font("Tahoma", 17F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo3_lbl.ForeColor = System.Drawing.Color.White;
            this.servo3_lbl.Location = new System.Drawing.Point(31, 117);
            this.servo3_lbl.Name = "servo3_lbl";
            this.servo3_lbl.Size = new System.Drawing.Size(30, 40);
            this.servo3_lbl.Text = "3";
            this.servo3_lbl.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // servo4_pct
            // 
            this.servo4_pct.AutoSize = true;
            this.servo4_pct.BackColor = System.Drawing.Color.Transparent;
            this.servo4_pct.Font = new System.Drawing.Font("Tahoma", 19.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo4_pct.ForeColor = System.Drawing.Color.Green;
            this.servo4_pct.Location = new System.Drawing.Point(15, 15);
            this.servo4_pct.Name = "servo4_lbl";
            this.servo4_pct.Size = new System.Drawing.Size(77, 40);
            this.servo4_pct.TabIndex = 4;
            this.servo4_pct.Text = "0%";
            this.servo4_pct.TextAlign = System.Drawing.ContentAlignment.BottomRight;
            // 
            // servo4_lbl
            // 
            this.servo4_lbl.BackColor = System.Drawing.Color.Transparent;
            this.servo4_lbl.Font = new System.Drawing.Font("Tahoma", 17F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.servo4_lbl.ForeColor = System.Drawing.Color.White;
            this.servo4_lbl.Location = new System.Drawing.Point(31, 49);
            this.servo4_lbl.Name = "servo4_lbl";
            this.servo4_lbl.Size = new System.Drawing.Size(30, 40);
            this.servo4_lbl.Text = "4";
            this.servo4_lbl.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // update_timer
            // 
            this.update_timer.Tick += new System.EventHandler(this.update_timer_Tick);
            // 
            // ThrustBalance
            // 
            this.AutoSize = true;
            this.BackColor = System.Drawing.SystemColors.ControlText;
            this.BackgroundImage = global::MissionPlanner.Properties.Resources.quadframesnormal_03;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.ClientSize = new System.Drawing.Size(240, 220);
            this.Controls.Add(this.servo1_pct);
            this.Controls.Add(this.servo1_lbl);
            this.Controls.Add(this.servo2_pct);
            this.Controls.Add(this.servo2_lbl);
            this.Controls.Add(this.servo3_pct);
            this.Controls.Add(this.servo3_lbl);
            this.Controls.Add(this.servo4_pct);
            this.Controls.Add(this.servo4_lbl);
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

        }

        #endregion
        private System.Windows.Forms.Label servo1_pct;
        private System.Windows.Forms.Label servo1_lbl;
        private System.Windows.Forms.Label servo2_pct;
        private System.Windows.Forms.Label servo2_lbl;
        private System.Windows.Forms.Label servo3_pct;
        private System.Windows.Forms.Label servo3_lbl;
        private System.Windows.Forms.Label servo4_pct;
        private System.Windows.Forms.Label servo4_lbl;
        private System.Windows.Forms.Timer update_timer;
        private FlightData.motorBalanceChecker motorBalanceChecker;
        private System.Drawing.Color[] colorGradient;
    }
}