using MissionPlanner;
using MissionPlanner.Plugin;
using GMap.NET.WindowsForms;
using System.Linq;
using System.Windows.Forms;
using WeblinkPlugin.Core.Http;
using WeblinkPlugin.UI;
using WeblinkPlugin.Core.Rc;
using System;

namespace WeblinkPlugin
{
    public class WeblinkPlugin : Plugin
    {
        private UIPanelManager _ui;
        private ServerManager _server;
        private MarkerManager _markers;
        private MapInteractionManager _mapUI;

        public override string Name => "Weblink Plugin";
        public override string Version => "0.5";
        public override string Author => "MUGA";

        public override bool Init() => true;

        public override bool Loaded()
        {
            _ui = new UIPanelManager();
            _ui.CreateAndAttachStatusPanel();

            var main = MainV2.instance;
            var flightData = main.FlightData as UserControl;
            if (flightData == null)
                return false;

            var map = flightData.Controls.Find("gMapControl1", true)
                                         .OfType<GMapControl>()
                                         .FirstOrDefault();

            if (map == null)
            {
                MessageBox.Show("GMapControl not found!", "Weblink Plugin",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _server = new ServerManager("http://127.0.0.1:9999");

            _server.TelemetryUpdated += (lat, lon) =>
            {
                _markers.UpdateStarlinkMarker(lat, lon);
            };

            _markers = new MarkerManager(map);

            _mapUI = new MapInteractionManager(map, _markers, _server);

            return true;
        }

        public override bool Exit()
        {
            _server?.Dispose();
            _ui?.Dispose();
            _markers?.Dispose();

            return true;
        }
    }
}
