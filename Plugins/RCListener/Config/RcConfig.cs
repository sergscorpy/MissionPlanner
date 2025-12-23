using System.Collections.Generic;

namespace RCListener.Config
{
    public class RangeAction
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public string Action { get; set; }
    }

    public class ChannelConfig
    {
        public string Name { get; set; } = string.Empty;
        public List<RangeAction> Ranges { get; set; } = new List<RangeAction>();
    }

    public static class RcConfig
    {
        public static readonly string UdpIp = "192.168.144.25";
        public static readonly int UdpPort = 37260;

        public static readonly Dictionary<int, ChannelConfig> Channels = new Dictionary<int, ChannelConfig>
        {
            [19] = new ChannelConfig
            {
                Name = "Down - Center",
                Ranges =
                {
                    new RangeAction { Min = 1750, Max = 2000, Action = "down" },
                    new RangeAction { Min = 1251, Max = 1749, Action = "down_45" },
                    new RangeAction { Min = 1000, Max = 1250, Action = "center" }
                }
            },
            [20] = new ChannelConfig
            {
                Name = "Zoom",
                Ranges =
                {
                    new RangeAction { Min = 1750, Max = 2000, Action = "zoom_in" },
                    new RangeAction { Min = 1250, Max = 1750, Action = "zoom_stop" },
                    new RangeAction { Min = 1000, Max = 1250, Action = "zoom_out" }
                }
            },
            [21] = new ChannelConfig
            {
                Name = "Pitch control",
                Ranges =
                {
                    new RangeAction { Min = 1700, Max = 1850, Action = "pitch_down_40" },
                    new RangeAction { Min = 1851, Max = 2000, Action = "pitch_down_80" },
                    new RangeAction { Min = 1475, Max = 1525, Action = "pitch_stop" },
                    new RangeAction { Min = 1150, Max = 1450, Action = "pitch_up_40" },
                    new RangeAction { Min = 1000, Max = 1149, Action = "pitch_up_80" }
                }
            }
        };
    }
}