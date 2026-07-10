using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GMap.NET;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class SituationMarkersForm : Form
    {
        readonly Color gridBackColor = Color.FromArgb(38, 39, 40);
        readonly Color rowBackColor = Color.FromArgb(40, 40, 40);
        readonly Color alternateRowBackColor = Color.FromArgb(48, 48, 48);
        readonly Color interestRowBackColor = Color.FromArgb(72, 68, 40);
        readonly Color selectionBackColor = Color.FromArgb(0, 122, 204);
        readonly SituationMarkersManager manager;
        readonly DataGridView grid = new DataGridView();
        readonly Label statusLabel = new Label();
        Button lockButton;
        bool refreshing;

        public SituationMarkersForm(SituationMarkersManager manager)
        {
            this.manager = manager;
            this.manager.SelectedMarkerChanged += Manager_SelectedMarkerChanged;
            Text = "Situation Markers";
            Width = 980;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            BuildLayout();
            RefreshGrid();
        }

        public void RefreshGrid()
        {
            if (refreshing)
                return;

            refreshing = true;
            try
            {
                grid.Refresh();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.DataBoundItem is SituationMarker marker)
                    {
                        row.DefaultCellStyle.BackColor = marker.IsInterest ? interestRowBackColor :
                            row.Index % 2 == 0 ? rowBackColor : alternateRowBackColor;
                        row.DefaultCellStyle.ForeColor = Color.White;
                        row.DefaultCellStyle.SelectionBackColor = selectionBackColor;
                        row.DefaultCellStyle.SelectionForeColor = Color.White;
                        row.DefaultCellStyle.Font = grid.Font;
                    }
                }

                UpdateLockState();
            }
            finally
            {
                refreshing = false;
            }

            SelectGridRow(manager.SelectedMarker);
        }

        public void SetStatus(string text)
        {
            statusLabel.Text = text ?? "";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            manager.SelectedMarkerChanged -= Manager_SelectedMarkerChanged;
            base.OnFormClosed(e);
        }

        void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = gridBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(6)
            };
            root.Controls.Add(toolbar, 0, 0);

            AddButton(toolbar, "Add Marker", (sender, args) => manager.AddMarker());
            AddButton(toolbar, "Add HOME Marker", (sender, args) =>
            {
                var home = MainV2.comPort.MAV.cs.HomeLocation;
                manager.AddOrUpdateHome(new PointLatLng(home.Lat, home.Lng), home.Alt);
            });
            AddButton(toolbar, "Elevation Profile", (sender, args) => manager.ShowElevationProfile(this));
            AddButton(toolbar, "Save", SaveMarkers);
            AddButton(toolbar, "Load", LoadMarkers);
            AddButton(toolbar, "Reset", ResetMarkers);
            lockButton = AddButton(toolbar, "Lock", ToggleLock);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = gridBackColor;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            ApplyGridStyle();
            grid.DataSource = manager.Markers;
            grid.CellClick += Grid_CellClick;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellParsing += Grid_CellParsing;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.KeyDown += Grid_KeyDown;
            grid.RowsAdded += (sender, args) => RefreshGrid();
            grid.DataBindingComplete += (sender, args) => RefreshGrid();
            root.Controls.Add(grid, 0, 1);

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Name",
                DataPropertyName = nameof(SituationMarker.Name),
                Width = 140
            });
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "HOME",
                DataPropertyName = nameof(SituationMarker.IsHome),
                ReadOnly = true,
                Width = 55
            });
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Interest",
                DataPropertyName = nameof(SituationMarker.IsInterest),
                Width = 70
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lat",
                DataPropertyName = nameof(SituationMarker.Lat),
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lng",
                DataPropertyName = nameof(SituationMarker.Lng),
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Altitude AMSL",
                DataPropertyName = nameof(SituationMarker.Altitude),
                Width = 110
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Alt Source",
                DataPropertyName = nameof(SituationMarker.AltitudeSource),
                ReadOnly = true,
                Width = 90
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Rel HOME",
                Name = "RelativeHome",
                ReadOnly = true,
                Width = 90
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "",
                Text = "Pick on map",
                UseColumnTextForButtonValue = true,
                Width = 95
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "",
                Text = "Delete",
                UseColumnTextForButtonValue = true,
                Width = 70
            });

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(8, 3, 8, 6);
            statusLabel.ForeColor = Color.White;
            statusLabel.BackColor = gridBackColor;
            root.Controls.Add(statusLabel, 0, 2);
        }

        void ApplyGridStyle()
        {
            grid.DefaultCellStyle.BackColor = rowBackColor;
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = selectionBackColor;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = alternateRowBackColor;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = selectionBackColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.BackColor = gridBackColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = gridBackColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            grid.GridColor = Color.FromArgb(120, 120, 120);
        }

        Button AddButton(Control parent, string text, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3)
            };
            button.Click += handler;
            parent.Controls.Add(button);
            return button;
        }

        void UpdateLockState()
        {
            if (lockButton != null)
            {
                lockButton.Text = manager.MarkersLocked ? "Unlock Drag" : "Lock Drag";
                lockButton.BackColor = manager.MarkersLocked ? Color.FromArgb(210, 90, 80) : SystemColors.Control;
            }

            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column is DataGridViewButtonColumn)
                    continue;

                column.ReadOnly = column.DataPropertyName == nameof(SituationMarker.IsHome) ||
                                  column.DataPropertyName == nameof(SituationMarker.AltitudeSource) ||
                                  column.Name == "RelativeHome";
            }
        }

        void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var marker = grid.Rows[e.RowIndex].DataBoundItem as SituationMarker;
            if (marker == null)
                return;

            var column = grid.Columns[e.ColumnIndex];
            if (column is DataGridViewButtonColumn && column.Index == grid.Columns.Count - 2)
            {
                manager.BeginPickOnMap(marker);
                return;
            }

            if (column is DataGridViewButtonColumn && column.Index == grid.Columns.Count - 1)
            {
                manager.RemoveMarker(marker);
                return;
            }

            if (column.DataPropertyName == nameof(SituationMarker.IsInterest))
                manager.SetInterestMarker(marker);
        }

        void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (refreshing || grid.CurrentRow == null)
                return;

            if (grid.CurrentCell != null && IsButtonColumn(grid.CurrentCell.ColumnIndex))
                return;

            if (grid.CurrentRow.DataBoundItem is SituationMarker marker)
                manager.SelectMarker(marker);
        }

        bool IsButtonColumn(int columnIndex)
        {
            return columnIndex >= 0 &&
                   columnIndex < grid.Columns.Count &&
                   grid.Columns[columnIndex] is DataGridViewButtonColumn;
        }

        void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete)
                return;

            if (manager.DeleteSelectedMarker())
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        void Manager_SelectedMarkerChanged(object sender, EventArgs e)
        {
            SelectGridRow(manager.SelectedMarker);
        }

        void SelectGridRow(SituationMarker selectedMarker)
        {
            if (refreshing)
                return;

            refreshing = true;
            try
            {
                grid.ClearSelection();
                if (selectedMarker == null)
                {
                    grid.CurrentCell = null;
                    return;
                }

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!(row.DataBoundItem is SituationMarker marker) || marker.Id != selectedMarker.Id)
                        continue;

                    row.Selected = true;
                    if (row.Cells.Count > 0)
                        grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (refreshing || e.RowIndex < 0)
                return;

            var marker = grid.Rows[e.RowIndex].DataBoundItem as SituationMarker;
            if (marker == null)
                return;

            var column = grid.Columns[e.ColumnIndex];
            if (column.DataPropertyName == nameof(SituationMarker.Name))
            {
                manager.NotifyMarkerEdited();
                RefreshGrid();
                return;
            }

            if (column.DataPropertyName == nameof(SituationMarker.Lat) ||
                column.DataPropertyName == nameof(SituationMarker.Lng))
            {
                manager.UpdateMarkerCoordinatesFromTable(marker, ParseNullable(marker.Lat), ParseNullable(marker.Lng));
                return;
            }

            if (column.DataPropertyName == nameof(SituationMarker.Altitude) && marker.Altitude.HasValue)
                manager.SetManualAltitude(marker, marker.Altitude.Value);
        }

        void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var marker = grid.Rows[e.RowIndex].DataBoundItem as SituationMarker;
            if (marker == null)
                return;

            if (grid.Columns[e.ColumnIndex].Name == "RelativeHome")
            {
                var relative = manager.GetRelativeToHome(marker);
                e.Value = relative.HasValue ? FormatAltitude(relative.Value) : "";
                e.FormattingApplied = true;
            }
            else if (e.Value is double value)
            {
                e.Value = value.ToString("0.000000", CultureInfo.InvariantCulture);
                e.FormattingApplied = true;
            }
        }

        void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            var propertyName = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (propertyName != nameof(SituationMarker.Lat) &&
                propertyName != nameof(SituationMarker.Lng) &&
                propertyName != nameof(SituationMarker.Altitude))
                return;

            var text = Convert.ToString(e.Value, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                e.Value = null;
                e.ParsingApplied = true;
                return;
            }

            text = text.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                e.Value = value;
                e.ParsingApplied = true;
            }
        }

        double? ParseNullable(double? value)
        {
            return value;
        }

        string FormatAltitude(double value)
        {
            var converted = value * CurrentState.multiplieralt;
            return (converted >= 0 ? "+" : "") + converted.ToString("0", CultureInfo.InvariantCulture) + " " + CurrentState.AltUnit;
        }

        void SaveMarkers(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Marker files (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = "situation-markers.json";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                manager.SaveToFile(dialog.FileName);
            }
        }

        void LoadMarkers(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Marker files (*.json)|*.json|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                manager.LoadFromFile(dialog.FileName);
            }
        }

        void ResetMarkers(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Reset all situation markers and clear autosave?", "Situation Markers",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            manager.ResetMarkers();
        }

        void ToggleLock(object sender, EventArgs e)
        {
            manager.SetMarkersLocked(!manager.MarkersLocked);
        }
    }
}
