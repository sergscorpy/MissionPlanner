using System.Collections.Generic;
using System.Linq;
using System.Text;
using RCListener.Config;

namespace RCListener.Camera
{
    public static class CameraProfiles
    {
        public const string DefaultCameraName = "SiYi A8 mini";

        public static readonly IReadOnlyList<ICameraProfile> Available = new List<ICameraProfile>
        {
            BuildSiyiA8Mini(),
            BuildTopotek()
        };

        public static ICameraProfile GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Available.FirstOrDefault(profile => profile.Name == DefaultCameraName) ?? Available[0];

            return Available.FirstOrDefault(profile => profile.Name == name)
                   ?? Available.FirstOrDefault(profile => profile.Name == DefaultCameraName)
                   ?? Available[0];
        }

        private static ICameraProfile BuildSiyiA8Mini()
        {
            var channels = new Dictionary<int, ChannelConfig>
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

            var commands = new Dictionary<string, byte[]>
            {
                ["down"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x08, 0x04 },
                ["down_45"] = new byte[] { 0x55, 0x66, 0x01, 0x04, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x3E, 0xFE },
                ["center"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x08, 0x01 },
                ["zoom_in"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0x01 },
                ["zoom_stop"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0x00 },
                ["zoom_out"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0xFF },
                ["pitch_up_40"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0xD8 },
                ["pitch_up_80"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0xB0 },
                ["pitch_stop"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00 },
                ["pitch_down_40"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x28 },
                ["pitch_down_80"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x50 }
            };

            return new CameraProfile(
                DefaultCameraName,
                "192.168.144.25",
                37260,
                true,
                channels,
                commands);
        }

        private static ICameraProfile BuildTopotek()
        {
            var channels = new Dictionary<int, ChannelConfig>
            {
                [19] = new ChannelConfig
                {
                    Name = "Down - Center",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "down" },
                        new RangeAction { Min = 1000, Max = 1249, Action = "center" }
                    }
                },
                [20] = new ChannelConfig
                {
                    Name = "Zoom",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "zoom_out" },
                        new RangeAction { Min = 1250, Max = 1750, Action = "zoom_stop" },
                        new RangeAction { Min = 1000, Max = 1249, Action = "zoom_in" }
                    }
                },
                [21] = new ChannelConfig
                {
                    Name = "Pitch control",
                    Ranges =
                    {
                        new RangeAction { Min = 1751, Max = 2000, Action = "pitch_down" },
                        new RangeAction { Min = 1250, Max = 1750, Action = "pitch_stop" },
                        new RangeAction { Min = 1000, Max = 1249, Action = "pitch_up" }
                    }
                },
                [22] = new ChannelConfig
                {
                    Name = "Thermal control",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "termal_next" },
                        //new RangeAction { Min = 1250, Max = 1750, Action = "termal_next1" },
                        //new RangeAction { Min = 1000, Max = 1249, Action = "termal_next2" }
                    }
                },
                [23] = new ChannelConfig
                {
                    Name = "Pip control",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "pip_change" },
                        new RangeAction { Min = 1250, Max = 1749, Action = "pip_change1" },
                        new RangeAction { Min = 1000, Max = 1249, Action = "pip_change2" }
                    }
                }
            };

            var commands = new Dictionary<string, byte[]>
            {
                ["down"] = Encoding.ASCII.GetBytes("#TPPG2wPTZ0A76"),
                ["center"] = Encoding.ASCII.GetBytes("#TPPG2wPTZ056A"),
                ["zoom_in"] = Encoding.ASCII.GetBytes("#TPPM2wZMC0259"),
                ["zoom_stop"] = Encoding.ASCII.GetBytes("#TPPM2wZMC0057"),
                ["zoom_out"] = Encoding.ASCII.GetBytes("#TPPM2wZMC0158"),
                ["pitch_up"] = Encoding.ASCII.GetBytes("#TPUG2wGSPE26D"),
                ["pitch_stop"] = Encoding.ASCII.GetBytes("#TPPG2wPTZ0065"),
                ["pitch_down"] = Encoding.ASCII.GetBytes("#TPUG2wGSP1E6C"),
                ["termal_next"] = Encoding.ASCII.GetBytes("#TPPD2wIMG0A52"),
                ["termal_next1"] = Encoding.ASCII.GetBytes("#TPPD2wIMG0A52"),
                ["termal_next2"] = Encoding.ASCII.GetBytes("#TPPD2wIMG0A52"),
                ["pip_change"] = Encoding.ASCII.GetBytes("#TPPD2wPIP0A5E"),
                ["pip_change1"] = Encoding.ASCII.GetBytes("#TPPD2wPIP0A5E"),
                ["pip_change2"] = Encoding.ASCII.GetBytes("#TPPD2wPIP0A5E")
            };

            return new CameraProfile(
                "TOPOTEK",
                "192.168.144.108",
                9003,
                false,
                channels,
                commands);
        }
    }
}