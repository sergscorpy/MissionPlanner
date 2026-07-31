using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GeoUtility.GeoSystem;
using GMap.NET;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public class FlyToCoordsForm : Form
    {
        const string CoordinateSystemSettingKey = "flytocoordscoord";

        readonly ComboBox coordinateSystemComboBox = new ComboBox();
        readonly Label firstLabel = new Label();
        readonly Label secondLabel = new Label();
        readonly Label thirdLabel = new Label();
        readonly Label altitudeLabel = new Label();
        readonly TextBox firstTextBox = new TextBox();
        readonly TextBox secondTextBox = new TextBox();
        readonly TextBox thirdTextBox = new TextBox();
        readonly TextBox altitudeTextBox = new TextBox();
        readonly MyButton okButton = new MyButton();
        readonly MyButton cancelButton = new MyButton();

        bool refreshing;
        bool showValidationErrors;
        Coords.CoordsSystems previousCoordinateSystem = Coords.CoordsSystems.GEO;
        double? latitude;
        double? longitude;

        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public double? Altitude { get; private set; }

        Coords.CoordsSystems CurrentCoordinateSystem
        {
            get
            {
                if (Enum.TryParse(Convert.ToString(coordinateSystemComboBox.SelectedItem, CultureInfo.InvariantCulture),
                        out Coords.CoordsSystems system))
                    return system;

                return Coords.CoordsSystems.GEO;
            }
        }

        public FlyToCoordsForm(PointLatLng defaultPosition)
        {
            Text = "Enter Fly To Coords";
            Width = 390;
            Height = 230;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            if (defaultPosition.Lat != 0 || defaultPosition.Lng != 0)
            {
                latitude = defaultPosition.Lat;
                longitude = defaultPosition.Lng;
            }

            BuildLayout();
            LoadCoordinateSystem();
            RefreshCoordinateFields();
            ThemeManager.ApplyThemeTo(this);
        }

        void BuildLayout()
        {
            var systemLabel = new Label
            {
                Text = "Coordinate system",
                AutoSize = true,
                Location = new Point(12, 15)
            };

            coordinateSystemComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            coordinateSystemComboBox.Items.Add(Coords.CoordsSystems.GEO.ToString());
            coordinateSystemComboBox.Items.Add(Coords.CoordsSystems.UTM.ToString());
            coordinateSystemComboBox.Items.Add(Coords.CoordsSystems.MGRS.ToString());
            coordinateSystemComboBox.Location = new Point(130, 11);
            coordinateSystemComboBox.Width = 90;
            coordinateSystemComboBox.SelectedIndexChanged += CoordinateSystemComboBox_SelectedIndexChanged;

            firstLabel.AutoSize = true;
            firstLabel.Location = new Point(12, 52);
            firstTextBox.Location = new Point(130, 49);
            firstTextBox.Width = 220;

            secondLabel.AutoSize = true;
            secondLabel.Location = new Point(12, 80);
            secondTextBox.Location = new Point(130, 77);
            secondTextBox.Width = 220;

            thirdLabel.AutoSize = true;
            thirdLabel.Location = new Point(12, 108);
            thirdTextBox.Location = new Point(130, 105);
            thirdTextBox.Width = 220;

            altitudeLabel.Text = "Altitude";
            altitudeLabel.AutoSize = true;
            altitudeLabel.Location = new Point(12, 136);
            altitudeTextBox.Location = new Point(130, 133);
            altitudeTextBox.Width = 220;

            okButton.Text = "OK";
            okButton.Location = new Point(194, 158);
            okButton.Size = new Size(75, 25);
            okButton.Click += OkButton_Click;

            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(275, 158);
            cancelButton.Size = new Size(75, 25);
            cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(systemLabel);
            Controls.Add(coordinateSystemComboBox);
            Controls.Add(firstLabel);
            Controls.Add(firstTextBox);
            Controls.Add(secondLabel);
            Controls.Add(secondTextBox);
            Controls.Add(thirdLabel);
            Controls.Add(thirdTextBox);
            Controls.Add(altitudeLabel);
            Controls.Add(altitudeTextBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        void LoadCoordinateSystem()
        {
            var system = Settings.Instance.ContainsKey(CoordinateSystemSettingKey)
                ? Settings.Instance[CoordinateSystemSettingKey]
                : Coords.CoordsSystems.GEO.ToString();

            coordinateSystemComboBox.SelectedItem = system;
            if (coordinateSystemComboBox.SelectedIndex < 0)
                coordinateSystemComboBox.SelectedItem = Coords.CoordsSystems.GEO.ToString();

            previousCoordinateSystem = CurrentCoordinateSystem;
        }

        void CoordinateSystemComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (refreshing)
                return;

            if (!TryReadCoordinateFields(previousCoordinateSystem, true))
            {
                refreshing = true;
                try
                {
                    coordinateSystemComboBox.SelectedItem = previousCoordinateSystem.ToString();
                }
                finally
                {
                    refreshing = false;
                }

                return;
            }

            Settings.Instance[CoordinateSystemSettingKey] = Convert.ToString(coordinateSystemComboBox.SelectedItem,
                CultureInfo.InvariantCulture);
            previousCoordinateSystem = CurrentCoordinateSystem;
            RefreshCoordinateFields();
        }

        void RefreshCoordinateFields()
        {
            refreshing = true;
            try
            {
                switch (CurrentCoordinateSystem)
                {
                    case Coords.CoordsSystems.UTM:
                        firstLabel.Text = "Zone";
                        secondLabel.Text = "Easting";
                        thirdLabel.Text = "Northing";
                        thirdLabel.Visible = true;
                        thirdTextBox.Visible = true;
                        FormatUtm();
                        break;

                    case Coords.CoordsSystems.MGRS:
                        firstLabel.Text = "MGRS";
                        secondLabel.Text = "";
                        thirdLabel.Text = "";
                        secondLabel.Visible = false;
                        secondTextBox.Visible = false;
                        thirdLabel.Visible = false;
                        thirdTextBox.Visible = false;
                        FormatMgrs();
                        return;

                    default:
                        firstLabel.Text = "Lat";
                        secondLabel.Text = "Lng";
                        thirdLabel.Text = "";
                        thirdLabel.Visible = false;
                        thirdTextBox.Visible = false;
                        FormatGeo();
                        break;
                }

                secondLabel.Visible = true;
                secondTextBox.Visible = true;
            }
            finally
            {
                refreshing = false;
            }
        }

        void FormatGeo()
        {
            firstTextBox.Text = latitude.HasValue
                ? latitude.Value.ToString("0.000000", CultureInfo.InvariantCulture)
                : "";
            secondTextBox.Text = longitude.HasValue
                ? longitude.Value.ToString("0.000000", CultureInfo.InvariantCulture)
                : "";
            thirdTextBox.Text = "";
        }

        void FormatUtm()
        {
            if (!latitude.HasValue || !longitude.HasValue)
            {
                firstTextBox.Text = "";
                secondTextBox.Text = "";
                thirdTextBox.Text = "";
                return;
            }

            var point = new PointLatLngAlt(latitude.Value, longitude.Value);
            var zone = point.GetUTMZone();
            var utm = point.ToUTM(zone);
            firstTextBox.Text = zone.ToString("0N;0S", CultureInfo.InvariantCulture);
            secondTextBox.Text = utm[0].ToString("0.000", CultureInfo.InvariantCulture);
            thirdTextBox.Text = utm[1].ToString("0.000", CultureInfo.InvariantCulture);
        }

        void FormatMgrs()
        {
            if (!latitude.HasValue || !longitude.HasValue)
            {
                firstTextBox.Text = "";
                return;
            }

            firstTextBox.Text = ((MGRS)new Geographic(longitude.Value, latitude.Value)).ToString();
        }

        void OkButton_Click(object sender, EventArgs e)
        {
            showValidationErrors = true;
            try
            {
                if (!TryReadCoordinateFields(false))
                    return;

                Latitude = latitude.Value;
                Longitude = longitude.Value;
                Altitude = null;

                if (!string.IsNullOrWhiteSpace(altitudeTextBox.Text))
                {
                    if (!TryParseDouble(altitudeTextBox.Text, out var altitude))
                    {
                        SetStatus("Invalid altitude.");
                        return;
                    }

                    Altitude = altitude;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                showValidationErrors = false;
            }
        }

        bool TryReadCoordinateFields(bool allowEmpty)
        {
            return TryReadCoordinateFields(CurrentCoordinateSystem, allowEmpty);
        }

        bool TryReadCoordinateFields(Coords.CoordsSystems system, bool allowEmpty)
        {
            switch (system)
            {
                case Coords.CoordsSystems.UTM:
                    return TryReadUtm(allowEmpty);

                case Coords.CoordsSystems.MGRS:
                    return TryReadMgrs(allowEmpty);

                default:
                    return TryReadGeo(allowEmpty);
            }
        }

        bool TryReadGeo(bool allowEmpty)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(firstTextBox.Text) && string.IsNullOrWhiteSpace(secondTextBox.Text))
                return true;

            if (!TryParseDouble(firstTextBox.Text, out var lat))
            {
                SetStatus("Invalid latitude.");
                return false;
            }

            if (!TryParseDouble(secondTextBox.Text, out var lng))
            {
                SetStatus("Invalid longitude.");
                return false;
            }

            latitude = lat;
            longitude = lng;
            SetStatus("");
            return true;
        }

        bool TryReadUtm(bool allowEmpty)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(firstTextBox.Text) &&
                string.IsNullOrWhiteSpace(secondTextBox.Text) && string.IsNullOrWhiteSpace(thirdTextBox.Text))
                return true;

            if (!TryParseUtmZone(firstTextBox.Text, out var zone))
            {
                SetStatus("Invalid UTM zone.");
                return false;
            }

            if (!TryParseDouble(secondTextBox.Text, out var easting))
            {
                SetStatus("Invalid UTM easting.");
                return false;
            }

            if (!TryParseDouble(thirdTextBox.Text, out var northing))
            {
                SetStatus("Invalid UTM northing.");
                return false;
            }

            try
            {
                var point = new utmpos(easting, northing, zone).ToLLA();
                latitude = point.Lat;
                longitude = point.Lng;
                SetStatus("");
                return true;
            }
            catch
            {
                SetStatus("Invalid UTM coordinates.");
                return false;
            }
        }

        bool TryReadMgrs(bool allowEmpty)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(firstTextBox.Text))
                return true;

            try
            {
                var mgrs = new MGRS(firstTextBox.Text.Trim());
                var geographic = mgrs.ConvertTo<Geographic>();
                latitude = geographic.Latitude;
                longitude = geographic.Longitude;
                SetStatus("");
                return true;
            }
            catch
            {
                SetStatus("Invalid MGRS coordinates.");
                return false;
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
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        void SetStatus(string text)
        {
            if (showValidationErrors && !string.IsNullOrEmpty(text))
                CustomMessageBox.Show(text, "Error");
        }
    }
}
