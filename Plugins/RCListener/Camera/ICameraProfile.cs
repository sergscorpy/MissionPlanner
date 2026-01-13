using System.Collections.Generic;
using RCListener.Config;

namespace RCListener.Camera
{
    public interface ICameraProfile
    {
        string Name { get; }
        string UdpIp { get; }
        int UdpPort { get; }
        bool AppendCrc16 { get; }
        IReadOnlyDictionary<int, ChannelConfig> Channels { get; }
        bool TryGetCommand(string action, out byte[] payload);
    }
}