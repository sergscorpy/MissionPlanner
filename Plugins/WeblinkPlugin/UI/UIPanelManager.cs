using MissionPlanner;
using System;
using System.Linq;
using System.Windows.Forms;

namespace WeblinkPlugin.UI
{
    public class UIPanelManager : IDisposable
    {
        public StatusPanel StatusPanel { get; private set; }

        public StatusPanel CreateAndAttachStatusPanel()
        {
            if (StatusPanel != null)
                return StatusPanel;

            var main = MainV2.instance;

            if (!(main.FlightData is UserControl flightData)) return null;

            if (!(flightData.Controls["MainH"] is SplitContainer mainH)) return null;

            if (!(mainH.Panel1.Controls["SubMainLeft"] is SplitContainer subMainLeft)) return null;

            var oldPanel = subMainLeft.Panel2;

            var controlsToMove = oldPanel.Controls.Cast<Control>().ToArray();

            var nestedSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 3,
                BorderStyle = BorderStyle.None,
                IsSplitterFixed = false,
                Name = "SubMainNested"
            };

            nestedSplit.Panel1MinSize = 0;
            nestedSplit.SplitterDistance = 15;

            StatusPanel = new StatusPanel
            {
                Dock = DockStyle.Fill
            };

            nestedSplit.Panel1.Controls.Add(StatusPanel);

            foreach (var c in controlsToMove)
                nestedSplit.Panel2.Controls.Add(c);

            oldPanel.Controls.Clear();
            oldPanel.Controls.Add(nestedSplit);

            return StatusPanel;
        }

        public void Dispose()
        {
            try
            {
                if (StatusPanel != null)
                {
                    var parent = StatusPanel.Parent;
                    parent?.Controls.Remove(StatusPanel);

                    StatusPanel.Dispose();
                    StatusPanel = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UIPanelManager] Dispose error: {ex.Message}");
            }
        }
    }
}
