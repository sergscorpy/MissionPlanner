using System;
using System.Collections.Generic;
using MissionPlanner;
using RCListener.Logging;
using RCListener.Processing;
using RCListener.Transport;

namespace RCListener.Camera
{
    public class CameraSelectionService
    {
        private const string CameraSettingKey = "rc_listener_camera";

        private readonly ILogger log;
        private readonly ChannelProcessor channelProcessor;
        private readonly GimbalCommandSender gimbalSender;

        public CameraSelectionService(ILogger log, ChannelProcessor channelProcessor, GimbalCommandSender gimbalSender)
        {
            this.log = log;
            this.channelProcessor = channelProcessor;
            this.gimbalSender = gimbalSender;
        }

        public event Action<ICameraProfile> CameraChanged;

        public IReadOnlyList<ICameraProfile> Profiles => CameraProfiles.Available;

        public ICameraProfile Current { get; private set; }

        public void Initialize()
        {
            var saved = Settings.Instance[CameraSettingKey];
            SetCamera(saved, persistSelection: false);
        }

        public void SetCamera(string name, bool persistSelection = true)
        {
            var profile = CameraProfiles.GetByName(name);
            if (profile == null)
                return;

            ApplyProfile(profile, persistSelection);
        }

        private void ApplyProfile(ICameraProfile profile, bool persistSelection)
        {
            if (profile == null)
                return;

            Current = profile;
            channelProcessor.UpdateChannelConfig(profile.Channels);
            gimbalSender.SetProfile(profile);

            if (persistSelection)
                Settings.Instance[CameraSettingKey] = profile.Name;

            log?.Log($"[CAM] Selected camera profile: {profile.Name} ({profile.UdpIp}:{profile.UdpPort})");

            try
            {
                CameraChanged?.Invoke(profile);
            }
            catch (Exception ex)
            {
                log?.Log($"[CAM] CameraChanged handler error: {ex.Message}");
            }
        }
    }
}