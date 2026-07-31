using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GeoUtility.GeoSystem;
using GMap.NET;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class SituationMarkersForm : Form
    {
        readonly Color gridBackColor = Color.FromArgb(38, 39, 40);
        readonly Color rowBackColor = Color.FromArgb(40, 40, 40);
        readonly Color alternateRowBackColor = Color.FromArgb(48, 48, 48);
        readonly Color targetRowBackColor = Color.FromArgb(72, 68, 40);
        readonly Color selectionBackColor = Color.FromArgb(0, 122, 204);
        readonly SituationMarkersManager manager;
        readonly MarkerGrid grid = new MarkerGrid();
        readonly Label statusLabel = new Label();
        readonly ComboBox coordinateSystemComboBox = new ComboBox();
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
                        row.DefaultCellStyle.BackColor = marker.IsInterest ? targetRowBackColor :
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
            AddButton(toolbar, "Save", SaveMarkers);
            AddButton(toolbar, "Load", LoadMarkers);
            AddButton(toolbar, "Reset", ResetMarkers);
            AddCoordinateSystemSelector(toolbar);

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
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
            ApplyGridStyle();
            grid.DataSource = manager.Markers;
            grid.CellClick += Grid_CellClick;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellParsing += Grid_CellParsing;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.KeyDown += Grid_KeyDown;
            grid.CopyCoordinatesRequested = CopySelectedMarkerCoordinates;
            grid.RowsAdded += (sender, args) => RefreshGrid();
            grid.DataBindingComplete += (sender, args) => RefreshGrid();
            root.Controls.Add(grid, 0, 1);

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Name",
                DataPropertyName = nameof(SituationMarker.Name),
                Width = 140
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "Target",
                Name = "Target",
                Text = "Set",
                UseColumnTextForButtonValue = false,
                Width = 70
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lat",
                Name = "Lat",
                DataPropertyName = nameof(SituationMarker.Lat),
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lng",
                Name = "Lng",
                DataPropertyName = nameof(SituationMarker.Lng),
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Zone",
                Name = "UtmZone",
                Width = 65
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Easting",
                Name = "UtmEasting",
                Width = 105
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Northing",
                Name = "UtmNorthing",
                Width = 105
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "MGRS",
                Name = "Mgrs",
                Width = 210
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Altitude",
                DataPropertyName = nameof(SituationMarker.Altitude),
                Width = 110
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
                Name = "PickOnMap",
                Text = "Pick on map",
                UseColumnTextForButtonValue = true,
                Width = 95
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "",
                Name = "Delete",
                Text = "Delete",
                UseColumnTextForButtonValue = true,
                Width = 70
            });

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(8, 3, 8, 6);
            statusLabel.ForeColor = Color.White;
            statusLabel.BackColor = gridBackColor;
            root.Controls.Add(statusLabel, 0, 2);

            UpdateCoordinateColumns();
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

        void AddCoordinateSystemSelector(Control parent)
        {
            coordinateSystemComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            coordinateSystemComboBox.Width = 70;
            coordinateSystemComboBox.Margin = new Padding(10, 4, 3, 3);
            coordinateSystemComboBox.Items.AddRange(Enum.GetNames(typeof(Coords.CoordsSystems)));
            coordinateSystemComboBox.SelectedItem = Coords.CoordsSystems.GEO.ToString();
            coordinateSystemComboBox.SelectedIndexChanged += (sender, args) =>
            {
                UpdateCoordinateColumns();
                grid.Refresh();
            };
            parent.Controls.Add(coordinateSystemComboBox);
        }

        void UpdateCoordinateColumns()
        {
            if (grid.Columns.Count == 0)
                return;

            var system = CurrentCoordinateSystem;
            grid.Columns["Lat"].Visible = system == Coords.CoordsSystems.GEO;
            grid.Columns["Lng"].Visible = system == Coords.CoordsSystems.GEO;
            grid.Columns["UtmZone"].Visible = system == Coords.CoordsSystems.UTM;
            grid.Columns["UtmEasting"].Visible = system == Coords.CoordsSystems.UTM;
            grid.Columns["UtmNorthing"].Visible = system == Coords.CoordsSystems.UTM;
            grid.Columns["Mgrs"].Visible = system == Coords.CoordsSystems.MGRS;
        }

        Coords.CoordsSystems CurrentCoordinateSystem
        {
            get
            {
                if (Enum.TryParse(coordinateSystemComboBox.SelectedItem as string,
                        out Coords.CoordsSystems system))
                    return system;

                return Coords.CoordsSystems.GEO;
            }
        }

        void UpdateLockState()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column is DataGridViewButtonColumn)
                    continue;

                column.ReadOnly = column.Name == "Target" || column.Name == "RelativeHome";
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
            if (column.Name == "Target")
            {
                manager.SetInterestMarker(marker);
                return;
            }

            if (column.Name == "PickOnMap")
            {
                manager.BeginPickOnMap(marker);
                return;
            }

            if (column.Name == "Delete")
            {
                manager.RemoveMarker(marker);
                return;
            }
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

        bool CopySelectedMarkerCoordinates()
        {
            if (grid.IsCurrentCellInEditMode)
                return false;

            var marker = GetClipboardMarker();
            if (marker == null || !HasCoordinates(marker))
                return false;

            var text = FormatCoordinatesForClipboard(marker);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            Clipboard.SetText(text);
            SetStatus("Coordinates copied: " + text);
            return true;
        }

        SituationMarker GetClipboardMarker()
        {
            if (grid.CurrentRow?.DataBoundItem is SituationMarker currentMarker)
                return currentMarker;

            if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].DataBoundItem is SituationMarker selectedMarker)
                return selectedMarker;

            return manager.SelectedMarker;
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

            if (column.Name == "UtmZone" || column.Name == "UtmEasting" || column.Name == "UtmNorthing")
            {
                ApplyUtmCoordinates(marker, grid.Rows[e.RowIndex], column.Name);
                return;
            }

            if (column.Name == "Mgrs")
            {
                ApplyMgrsCoordinates(marker, grid.Rows[e.RowIndex]);
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
            else if (grid.Columns[e.ColumnIndex].Name == "Target")
            {
                e.Value = marker.IsInterest ? "Target" : "Set";
                e.FormattingApplied = true;
            }
            else if (grid.Columns[e.ColumnIndex].DataPropertyName == nameof(SituationMarker.Altitude) &&
                     e.Value is double altitude)
            {
                e.Value = altitude.ToString("0", CultureInfo.InvariantCulture);
                e.FormattingApplied = true;
            }
            else if (grid.Columns[e.ColumnIndex].Name == "UtmZone")
            {
                if (HasCoordinates(marker))
                {
                    e.Value = FormatUtmZone(marker);
                    e.FormattingApplied = true;
                }
            }
            else if (grid.Columns[e.ColumnIndex].Name == "UtmEasting")
            {
                if (HasCoordinates(marker))
                {
                    e.Value = FormatUtmCoordinate(marker, true);
                    e.FormattingApplied = true;
                }
            }
            else if (grid.Columns[e.ColumnIndex].Name == "UtmNorthing")
            {
                if (HasCoordinates(marker))
                {
                    e.Value = FormatUtmCoordinate(marker, false);
                    e.FormattingApplied = true;
                }
            }
            else if (grid.Columns[e.ColumnIndex].Name == "Mgrs")
            {
                if (HasCoordinates(marker))
                {
                    e.Value = FormatMgrs(marker);
                    e.FormattingApplied = true;
                }
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

        bool HasCoordinates(SituationMarker marker)
        {
            return marker.Lat.HasValue && marker.Lng.HasValue;
        }

        void ApplyUtmCoordinates(SituationMarker marker, DataGridViewRow row, string editedColumnName)
        {
            var hasCurrentUtm = TryGetCurrentUtm(marker, out var zone, out var easting, out var northing);
            var hasZone = hasCurrentUtm;
            var hasEasting = hasCurrentUtm;
            var hasNorthing = hasCurrentUtm;

            var zoneText = Convert.ToString(row.Cells["UtmZone"].Value, CultureInfo.CurrentCulture);
            var eastingText = Convert.ToString(row.Cells["UtmEasting"].Value, CultureInfo.CurrentCulture);
            var northingText = Convert.ToString(row.Cells["UtmNorthing"].Value, CultureInfo.CurrentCulture);

            if (ShouldUseUtmCell(editedColumnName, "UtmZone", hasCurrentUtm) &&
                !string.IsNullOrWhiteSpace(zoneText) && !TryParseUtmZone(zoneText, out zone))
            {
                SetStatus("Invalid UTM zone.");
                RefreshGrid();
                return;
            }
            else if (ShouldUseUtmCell(editedColumnName, "UtmZone", hasCurrentUtm) &&
                     !string.IsNullOrWhiteSpace(zoneText))
            {
                hasZone = true;
            }

            if (ShouldUseUtmCell(editedColumnName, "UtmEasting", hasCurrentUtm) &&
                !string.IsNullOrWhiteSpace(eastingText) && !TryParseDouble(eastingText, out easting))
            {
                SetStatus("Invalid UTM easting.");
                RefreshGrid();
                return;
            }
            else if (ShouldUseUtmCell(editedColumnName, "UtmEasting", hasCurrentUtm) &&
                     !string.IsNullOrWhiteSpace(eastingText))
            {
                hasEasting = true;
            }

            if (ShouldUseUtmCell(editedColumnName, "UtmNorthing", hasCurrentUtm) &&
                !string.IsNullOrWhiteSpace(northingText) && !TryParseDouble(northingText, out northing))
            {
                SetStatus("Invalid UTM northing.");
                RefreshGrid();
                return;
            }
            else if (ShouldUseUtmCell(editedColumnName, "UtmNorthing", hasCurrentUtm) &&
                     !string.IsNullOrWhiteSpace(northingText))
            {
                hasNorthing = true;
            }

            if (!hasZone || !hasEasting || !hasNorthing)
            {
                SetStatus("Enter full UTM coordinates.");
                return;
            }

            try
            {
                var point = new utmpos(easting, northing, zone).ToLLA();
                manager.UpdateMarkerCoordinatesFromTable(marker, point.Lat, point.Lng);
                SetStatus("");
            }
            catch
            {
                SetStatus("Invalid UTM coordinates.");
                RefreshGrid();
            }
        }

        bool ShouldUseUtmCell(string editedColumnName, string cellColumnName, bool hasCurrentUtm)
        {
            return !hasCurrentUtm || editedColumnName == cellColumnName;
        }

        void ApplyMgrsCoordinates(SituationMarker marker, DataGridViewRow row)
        {
            var mgrsText = Convert.ToString(row.Cells["Mgrs"].Value, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(mgrsText))
            {
                SetStatus("Enter MGRS coordinates.");
                RefreshGrid();
                return;
            }

            try
            {
                var mgrs = new MGRS(mgrsText.Trim());
                var geographic = mgrs.ConvertTo<Geographic>();
                manager.UpdateMarkerCoordinatesFromTable(marker, geographic.Latitude, geographic.Longitude);
                SetStatus("");
            }
            catch
            {
                SetStatus("Invalid MGRS coordinates.");
                RefreshGrid();
            }
        }

        bool TryGetCurrentUtm(SituationMarker marker, out int zone, out double easting, out double northing)
        {
            zone = 0;
            easting = 0;
            northing = 0;

            if (!marker.Lat.HasValue || !marker.Lng.HasValue)
                return false;

            var point = new PointLatLngAlt(marker.Lat.Value, marker.Lng.Value);
            zone = point.GetUTMZone();
            var utm = point.ToUTM(zone);
            easting = utm[0];
            northing = utm[1];
            return true;
        }

        string FormatUtmZone(SituationMarker marker)
        {
            return TryGetCurrentUtm(marker, out var zone, out _, out _)
                ? zone.ToString("0N;0S", CultureInfo.InvariantCulture)
                : "";
        }

        string FormatUtmCoordinate(SituationMarker marker, bool easting)
        {
            return TryGetCurrentUtm(marker, out _, out var east, out var north)
                ? (easting ? east : north).ToString("0.000", CultureInfo.InvariantCulture)
                : "";
        }

        string FormatMgrs(SituationMarker marker)
        {
            if (!marker.Lat.HasValue || !marker.Lng.HasValue)
                return "";

            try
            {
                return ((MGRS)new Geographic(marker.Lng.Value, marker.Lat.Value)).ToString();
            }
            catch
            {
                return "";
            }
        }

        string FormatCoordinatesForClipboard(SituationMarker marker)
        {
            switch (CurrentCoordinateSystem)
            {
                case Coords.CoordsSystems.UTM:
                    return TryGetCurrentUtm(marker, out var zone, out var easting, out var northing)
                        ? string.Format(CultureInfo.InvariantCulture, "{0} {1:0.000} {2:0.000}",
                            zone.ToString("0N;0S", CultureInfo.InvariantCulture), easting, northing)
                        : "";

                case Coords.CoordsSystems.MGRS:
                    return FormatMgrs(marker);

                default:
                    return string.Format(CultureInfo.InvariantCulture, "{0:0.000000} {1:0.000000}",
                        marker.Lat.Value, marker.Lng.Value);
            }
        }

        bool TryParseUtmZone(string text, out int zone)
        {
            zone = 0;
            text = text.Trim().ToUpperInvariant();
            if (text.EndsWith("N", StringComparison.Ordinal))
                return int.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out zone);

            if (text.EndsWith("S", StringComparison.Ordinal) &&
                int.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out zone))
            {
                zone = -Math.Abs(zone);
                return true;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out zone);
        }

        bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsCopyShortcut(keyData) && CopySelectedMarkerCoordinates())
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        static bool IsCopyShortcut(Keys keyData)
        {
            return (keyData & Keys.KeyCode) == Keys.C && (keyData & Keys.Control) == Keys.Control;
        }

        class MarkerGrid : DataGridView
        {
            public Func<bool> CopyCoordinatesRequested { get; set; }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (IsCopyShortcut(keyData))
                {
                    if (CopyCoordinatesRequested != null && CopyCoordinatesRequested())
                        return true;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
    }
}
