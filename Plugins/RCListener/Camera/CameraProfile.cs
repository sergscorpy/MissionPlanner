using System.Collections.Generic;
using RCListener.Config;

namespace RCListener.Camera
{
    public class CameraProfile : ICameraProfile
    {
        private readonly Dictionary<string, byte[]> commands;

        public CameraProfile(
            string name,
            string udpIp,
            int udpPort,
            bool appendCrc16,
            IReadOnlyDictionary<int, ChannelConfig> channels,
            Dictionary<string, byte[]> commands)
        {
            Name = name;
            UdpIp = udpIp;
            UdpPort = udpPort;
            AppendCrc16 = appendCrc16;
            Channels = channels;
            this.commands = commands;
        }

        public string Name { get; }

        public string UdpIp { get; }

        public int UdpPort { get; }

        public bool AppendCrc16 { get; }

        public IReadOnlyDictionary<int, ChannelConfig> Channels { get; }

        public bool TryGetCommand(string action, out byte[] payload)
        {
            return commands.TryGetValue(action, out payload);
        }
    }
}