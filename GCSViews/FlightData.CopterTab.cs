using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private sealed class CopterParamDescriptor
        {
            public CopterParamDescriptor(string label, string paramName, decimal min, decimal max)
            {
                Label = label;
                ParamName = paramName;
                Min = min;
                Max = max;
            }

            public string Label { get; }
            public string ParamName { get; }
            public decimal Min { get; }
            public decimal Max { get; }
        }

        private const int CopterColumnCount = 4;
        private const int CopterRowCount = 15;
        private const int CopterRowHeight = 32;
        private const int CopterDataGridRowHeight = 96;
        private const int CopterTableWidth = 300;
        private const int CopterTablePadding = 4;
        private const int CopterTableMargin = 0;
        private const int CopterDataGridRowTemplateHeight = 35;
        private const int CopterDataGridRowIndex = 2;
        private const int CopterDataGridColumnSpan = 4;
        private const int CopterCustomColumnIndex = 1;
        private const int CopterCurrentColumnIndex = 2;

        private static readonly CopterParamDescriptor[] CopterParamDescriptors =
        {
            new CopterParamDescriptor("Angle Max", "ANGLE_MAX", 1000, 8000),
            new CopterParamDescriptor("Loit Speed", "LOIT_SPEED", 20, 50000),
            new CopterParamDescriptor("Mission Speed", "WPNAV_SPEED", 10, 50000)
        };

        private static readonly IReadOnlyDictionary<string, int> CopterParamRowLookup =
            CopterParamDescriptors
                .Select((descriptor, index) => new { descriptor.ParamName, Index = index })
                .ToDictionary(entry => entry.ParamName, entry => entry.Index);

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
            tableLayoutPanelCopter.SetColumnSpan(dataGridView, CopterDataGridColumnSpan);
            tableLayoutPanelCopter.SetRowSpan(dataGridView, 1);
            tableLayoutPanelCopter.Controls.Add(dataGridView, 0, CopterDataGridRowIndex);
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

            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            foreach (var descriptor in CopterParamDescriptors)
            {
                int rowIndex = dataGridView.Rows.Add(descriptor.Label, 0, 0);

                var cell = new DataGridViewNumericUpDownCell
                {
                    Value = dataGridView.Rows[rowIndex].Cells[1].Value,
                    Minimum = descriptor.Min,
                    Maximum = descriptor.Max
                };

                dataGridView.Rows[rowIndex].Cells[1] = cell;
            }

            dataGridView.RowTemplate.Height = CopterDataGridRowTemplateHeight;
            dataGridView.EditingControlShowing += DataGridView_EditingControlShowing;
        }

        private void UpdateCopterButtonState(Button button, bool enabled, Color backColor, bool resetAutoSize = false)
        {
            if (button == null)
            {
                return;
            }

            button.Enabled = enabled;
            button.BackColor = backColor;

            if (resetAutoSize)
            {
                button.AutoSize = false;
            }
        }

        private void EnsureControlAdded(Control control, int column, int row)
        {
            if (!tableLayoutPanelCopter.Controls.Contains(control))
            {
                tableLayoutPanelCopter.Controls.Add(control, column, row);
            }
        }

        private void EnsureControlRemoved(Control control)
        {
            if (tableLayoutPanelCopter.Controls.Contains(control))
            {
                tableLayoutPanelCopter.Controls.Remove(control);
            }
        }

        private void AddButton(string name, int column, int row)
        {
            var button = CreateButton(name);
            tableLayoutPanelCopter.Controls.Add(button, column, row);
            ListButtonsMods.Add(button);
        }

        private Button CreateButton(string name)
        {
            var button = new Button();
            button.Name = name.Replace(" ", "_");
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = colorDis;
            button.Font = fontBut;
            button.Text = name;
            button.AutoSize = false;
            button.BackColor = colorDis;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Margin = new Padding(3);
            button.Click += new EventHandler(this.ModeClick);

            return button;
        }

        private int GetCopterParamRowIndex(string paramName)
        {
            if (!CopterParamRowLookup.TryGetValue(paramName, out var rowIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(paramName), paramName, "Unknown copter param.");
            }

            return rowIndex;
        }

        private decimal GetCopterCustomParamValue(string paramName)
        {
            int rowIndex = GetCopterParamRowIndex(paramName);
            return Convert.ToDecimal(dataGridView.Rows[rowIndex].Cells[CopterCustomColumnIndex].Value);
        }

        private decimal GetCopterCurrentParamValue(string paramName)
        {
            int rowIndex = GetCopterParamRowIndex(paramName);
            return Convert.ToDecimal(dataGridView.Rows[rowIndex].Cells[CopterCurrentColumnIndex].Value);
        }

        private void SetCopterCustomParamValue(string paramName, decimal value)
        {
            int rowIndex = GetCopterParamRowIndex(paramName);
            dataGridView.Rows[rowIndex].Cells[CopterCustomColumnIndex].Value = value;
        }

        private void ShowCopterDataGridView()
        {
            if (!tableLayoutPanelCopter.Controls.Contains(dataGridView))
            {
                tableLayoutPanelCopter.Controls.Add(dataGridView, 0, CopterDataGridRowIndex);
            }

            if (tableLayoutPanelCopter.RowStyles.Count > CopterDataGridRowIndex)
            {
                tableLayoutPanelCopter.RowStyles[CopterDataGridRowIndex].Height = CopterDataGridRowHeight;
            }
        }

        private void HideCopterDataGridView()
        {
            if (tableLayoutPanelCopter.Controls.Contains(dataGridView))
            {
                tableLayoutPanelCopter.Controls.Remove(dataGridView);
            }

            if (tableLayoutPanelCopter.RowStyles.Count > CopterDataGridRowIndex)
            {
                tableLayoutPanelCopter.RowStyles[CopterDataGridRowIndex].Height = 0F;
            }
        }

        public class DataGridViewNumericUpDownCell : DataGridViewTextBoxCell
        {
            public decimal Minimum { get; set; }
            public decimal Maximum { get; set; }

            public DataGridViewNumericUpDownCell() : base()
            {
                Style.Format = string.Empty;
                Minimum = 0;
                Maximum = 1000000;
            }

            public override void InitializeEditingControl(int rowIndex, object initialFormattedValue,
                DataGridViewCellStyle dataGridViewCellStyle)
            {
                base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
                NumericUpDownEditingControl ctl = DataGridView.EditingControl as NumericUpDownEditingControl;
                if (ctl != null)
                {
                    ctl.Minimum = Minimum;
                    ctl.Maximum = Maximum;
                    ctl.Value = Math.Max(ctl.Minimum, Math.Min(ctl.Maximum, Convert.ToDecimal(Value)));
                }
            }

            public override Type EditType => typeof(NumericUpDownEditingControl);
            public override Type ValueType => typeof(decimal);
            public override object DefaultNewRowValue => 0m;
        }

        public class NumericUpDownEditingControl : NumericUpDown, IDataGridViewEditingControl
        {
            private DataGridView dataGridView;
            private bool valueChanged = false;
            private int rowIndex;

            public object EditingControlFormattedValue
            {
                get { return Value.ToString(); }
                set
                {
                    if (decimal.TryParse(value.ToString(), out decimal result))
                    {
                        Value = result;
                    }
                }
            }

            public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)
            {
                return Value.ToString();
            }

            public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
            {
                Font = dataGridViewCellStyle.Font;
                ForeColor = dataGridViewCellStyle.ForeColor;
                BackColor = dataGridViewCellStyle.BackColor;
            }

            public int EditingControlRowIndex
            {
                get { return rowIndex; }
                set { rowIndex = value; }
            }

            public bool EditingControlWantsInputKey(Keys key, bool dataGridViewWantsInputKey)
            {
                switch (key & Keys.KeyCode)
                {
                    case Keys.Left:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Right:
                        return true;
                    default:
                        return !dataGridViewWantsInputKey;
                }
            }

            public void PrepareEditingControlForEdit(bool selectAll)
            {
            }

            public bool RepositionEditingControlOnValueChange => false;

            public DataGridView EditingControlDataGridView
            {
                get { return dataGridView; }
                set { dataGridView = value; }
            }

            public bool EditingControlValueChanged
            {
                get { return valueChanged; }
                set { valueChanged = value; }
            }

            public Cursor EditingPanelCursor => base.Cursor;

            protected override void OnValueChanged(EventArgs eventargs)
            {
                valueChanged = true;
                EditingControlDataGridView.NotifyCurrentCellDirty(true);
                base.OnValueChanged(eventargs);
            }
        }

        private void DataGridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (e.Control is NumericUpDownEditingControl numericUpDown)
            {
                numericUpDown.Enter -= NumericUpDown_Enter;
                numericUpDown.Enter += NumericUpDown_Enter;
            }
        }

        private void NumericUpDown_Enter(object sender, EventArgs e)
        {
            if (sender is NumericUpDown numericUpDown)
            {
                numericUpDown.Select(0, numericUpDown.Text.Length);
            }
        }
    }
}