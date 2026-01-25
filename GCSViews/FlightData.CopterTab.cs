using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private const int CopterColumnCount = 4;
        private const int CopterRowCount = 15;
        private const int CopterRowHeight = 32;
        private const int CopterDataGridRowHeight = 96;
        private const int CopterTableWidth = 300;
        private const int CopterTablePadding = 4;
        private const int CopterTableMargin = 0;
        private const int CopterDataGridRowTemplateHeight = 35;

        private List<KeyValuePair<string, string>> BuildCopterModelItems()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Вампір", "vampire"),
                new KeyValuePair<string, string>("Воробєй", "sparrow")
            };
        }

        private void InitializeCopterTab()
        {
            tabCopter.Controls.Add(tableLayoutPanelCopter);
            tabCopter.Name = "tabCopter";
            tabCopter.Text = "Copter";
            tabCopter.UseVisualStyleBackColor = true;
        }

        private void InitializeCopterTableLayout()
        {
            float columnWidth = 100f / CopterColumnCount;
            int sizeHeight = CopterRowHeight * CopterRowCount;

            tableLayoutPanelCopter.Name = "tableLayoutPanelCopter";
            tableLayoutPanelCopter.RowCount = CopterRowCount;
            tableLayoutPanelCopter.ColumnCount = CopterColumnCount;
            tableLayoutPanelCopter.ColumnStyles.Clear();
            tableLayoutPanelCopter.RowStyles.Clear();

            for (int i = 0; i < CopterColumnCount; i++)
            {
                tableLayoutPanelCopter.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, columnWidth));
            }

            for (int i = 0; i < CopterRowCount; i++)
            {
                tableLayoutPanelCopter.RowStyles.Add(
                    new RowStyle(SizeType.Absolute, CopterRowHeight));
            }

            if (tableLayoutPanelCopter.RowStyles.Count > 2)
            {
                tableLayoutPanelCopter.RowStyles[2].Height = CopterDataGridRowHeight;
            }

            tableLayoutPanelCopter.Location = new Point(3, 3);
            tableLayoutPanelCopter.Dock = DockStyle.Top;
            tableLayoutPanelCopter.Size = new Size(CopterTableWidth, sizeHeight);
            tableLayoutPanelCopter.Margin = new Padding(CopterTableMargin);
            tableLayoutPanelCopter.Padding = new Padding(CopterTablePadding);
        }

        private void InitializeCopterModelSelector()
        {
            tableLayoutPanelCopter.Controls.Add(comboBoxDronModel, 2, 0);
            comboBoxDronModel.DataSource = _comboItems;
            comboBoxDronModel.DisplayMember = "Key";
            comboBoxDronModel.ValueMember = "Value";
            comboBoxDronModel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxDronModel.DropDownWidth = 100;
            comboBoxDronModel.FormattingEnabled = true;
            comboBoxDronModel.Dock = DockStyle.Fill;
            comboBoxDronModel.Font = fontNuveric;
            comboBoxDronModel.Margin = new Padding(3);
            comboBoxDronModel.SelectedIndexChanged += comboBoxDronModel_SelectedIndexChanged;
        }

        private void InitializeCopterModeCheckboxes()
        {
            tableLayoutPanelCopter.Controls.Add(chBox_ExpMod, 0, 0);
            chBox_ExpMod.Appearance = Appearance.Button;
            chBox_ExpMod.CheckAlign = ContentAlignment.MiddleLeft;
            chBox_ExpMod.Margin = new Padding(10, 2, 10, 2);
            chBox_ExpMod.AutoSize = true;
            chBox_ExpMod.Checked = false;
            chBox_ExpMod.Dock = DockStyle.Fill;
            chBox_ExpMod.Name = "chBox_ExpMod";
            chBox_ExpMod.Text = "Exp Mod";
            chBox_ExpMod.Font = new Font("Microsoft Sans Serif", 8F);
            chBox_ExpMod.TextAlign = ContentAlignment.MiddleCenter;
            chBox_ExpMod.UseVisualStyleBackColor = true;
            chBox_ExpMod.CheckedChanged += chBox_ExpMod_CheckedChanged;

            tableLayoutPanelCopter.Controls.Add(chBox_X9, 3, 0);
            chBox_X9.Appearance = Appearance.Button;
            chBox_X9.CheckAlign = ContentAlignment.MiddleLeft;
            chBox_X9.Margin = new Padding(10, 2, 10, 2);
            chBox_X9.AutoSize = true;
            chBox_X9.Checked = false;
            chBox_X9.Dock = DockStyle.Fill;
            chBox_X9.Name = "chBox_X9";
            chBox_X9.Text = "X9";
            chBox_X9.Font = fontBut;
            chBox_X9.TextAlign = ContentAlignment.MiddleCenter;
            chBox_X9.UseVisualStyleBackColor = true;
            chBox_X9.CheckedChanged += chBox_X9_CheckedChanged;
        }

        private void InitializeCopterDataGridView()
        {
            tableLayoutPanelCopter.SetColumnSpan(dataGridView, 4);
            tableLayoutPanelCopter.SetRowSpan(dataGridView, 1);
            tableLayoutPanelCopter.Controls.Add(dataGridView, 0, 2);
            dataGridView.AllowUserToAddRows = false;
            dataGridView.RowHeadersVisible = false;
            dataGridView.AllowUserToResizeColumns = false;
            dataGridView.AllowUserToResizeRows = false;
            dataGridView.ColumnCount = 3;
            dataGridView.ScrollBars = ScrollBars.None;
            dataGridView.BackgroundColor = Color.FromArgb(30, 30, 30);
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView.EditMode = DataGridViewEditMode.EditOnEnter;
            dataGridView.Dock = DockStyle.Fill;
            dataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dataGridView.Columns[0].Name = "Param";
            dataGridView.Columns[1].Name = "Custom";
            dataGridView.Columns[2].Name = "Current";
            dataGridView.Columns[2].ReadOnly = true;
            dataGridView.Columns[0].ReadOnly = true;

            dataGridView.DefaultCellStyle.BackColor = Color.FromArgb(45, 45, 45);
            dataGridView.DefaultCellStyle.ForeColor = Color.White;
            dataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 70, 70);
            dataGridView.DefaultCellStyle.SelectionForeColor = Color.White;

            dataGridView.Rows.Add("Angle Max", 0, 0);
            dataGridView.Rows.Add("Loit Speed", 0, 0);
            dataGridView.Rows.Add("Mission Speed", 0, 0);

            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                decimal minValue;
                decimal maxValue;

                if (row.Cells[0].Value?.ToString() == "Angle Max")
                {
                    minValue = 1000;
                    maxValue = 8000;
                }
                else if (row.Cells[0].Value?.ToString() == "Loit Speed")
                {
                    minValue = 20;
                    maxValue = 50000;
                }
                else if (row.Cells[0].Value?.ToString() == "Mission Speed")
                {
                    minValue = 10;
                    maxValue = 50000;
                }
                else
                {
                    minValue = 0;
                    maxValue = 0;
                }

                var cell = new DataGridViewNumericUpDownCell
                {
                    Value = row.Cells[1].Value,
                    Minimum = minValue,
                    Maximum = maxValue
                };

                row.Cells[1] = cell;
            }

            dataGridView.RowTemplate.Height = CopterDataGridRowTemplateHeight;
            dataGridView.EditingControlShowing += DataGridView_EditingControlShowing;
        }
    }
}