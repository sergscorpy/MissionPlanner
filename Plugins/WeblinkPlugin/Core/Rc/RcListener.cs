using MissionPlanner;
using System;
using System.Threading.Tasks;
using System.Timers;
using WeblinkPlugin.Core.Http;
using WeblinkPlugin.Core.Http.Storage;

namespace WeblinkPlugin.Core.Rc
{
    internal sealed class RcListener : IDisposable
    {
        private readonly Timer _pollTimer;
        private MAVLinkInterface _mavlink;
        private bool _subscribed;
        private DeviceClient _device;

        private readonly object _lock = new object();

        private string _pendingMode;
        private bool _sending;

        public RcListener()
        {
            _pollTimer = new Timer(500);
            _pollTimer.Elapsed += PollMavlink;
            _pollTimer.AutoReset = true;
            _pollTimer.Start();
        }

        public void SetDevice(DeviceClient device)
        {
            _device = device;

            if (_device != null)
            {
                _ = TrySendModeAsync();
            }
        }

        private void PollMavlink(object sender, ElapsedEventArgs e)
        {
            var mp = MainV2.comPort;

            if (mp?.BaseStream == null || !mp.BaseStream.IsOpen)
            {
                Unsubscribe();
                return;
            }

            if (!_subscribed)
            {
                _mavlink = mp;
                Subscribe();
            }
        }

        private void Subscribe()
        {
            if (_mavlink == null || _subscribed)
                { return; }

            _mavlink.OnPacketSent += OnPacketSent;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_mavlink == null || !_subscribed)
                { return; }

            _mavlink.OnPacketSent -= OnPacketSent;
            _subscribed = false;
        }

        private void OnPacketSent(object sender, MAVLink.MAVLinkMessage msg)
        {
            if (msg.msgid != (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE)
                return;

            MAVLink.mavlink_rc_channels_override_t rc;

            try
            {
                rc = msg.ToStructure<MAVLink.mavlink_rc_channels_override_t>();
            }
            catch
            {
                return;
            }

            int channel = SharedState.Instance.Channel.Value;
            int pwm = GetChannelPwm(rc, channel);

            if (pwm == 0)
                return;

            SharedState.Instance.UpdateChanelPwm(pwm);

            string newMode = DetectMode(pwm);

            if (newMode != SharedState.Instance.CurrentMode)
            {
                SharedState.Instance.UpdateCurrentMode(newMode);
                OnModeChanged(newMode);
            }
        }

        private static int GetChannelPwm(MAVLink.mavlink_rc_channels_override_t rc, int channel)
        {
            switch (channel)
            {
                case 1: return rc.chan1_raw;
                case 2: return rc.chan2_raw;
                case 3: return rc.chan3_raw;
                case 4: return rc.chan4_raw;
                case 5: return rc.chan5_raw;
                case 6: return rc.chan6_raw;
                case 7: return rc.chan7_raw;
                case 8: return rc.chan8_raw;
                case 9: return rc.chan9_raw;
                case 10: return rc.chan10_raw;
                case 11: return rc.chan11_raw;
                case 12: return rc.chan12_raw;
                case 13: return rc.chan13_raw;
                case 14: return rc.chan14_raw;
                case 15: return rc.chan15_raw;
                case 16: return rc.chan16_raw;
                default: return 0;
            }
        }

        private static string DetectMode (int pwm)
        {
            if (pwm >= 1000 && pwm < 1400)
                return "starlink";

            if (pwm >= 1400 && pwm < 1600)
                return "mavlink";

            if (pwm >= 1600 && pwm <= 2000)
                return "manual";

            return "invalid";
        }

        private void OnModeChanged(string mode)
        {
            lock (_lock)
            {
                _pendingMode = mode;
            }

            _ = TrySendModeAsync();
        }

        private async Task TrySendModeAsync()
        {
            string modeToSend;

            lock (_lock)
            {
                if (_sending)
                    return;

                if (_device == null || string.IsNullOrEmpty(_pendingMode))
                    return;

                _sending = true;
                modeToSend = _pendingMode;
            }

            bool success = false;

            try
            {
                success = await _device.SendModeAsync(modeToSend);
            }
            catch
            {
                success = false;
            }

            lock (_lock)
            {
                _sending = false;

                if (success && _pendingMode == modeToSend)
                {
                    _pendingMode = null;
                    return;
                }
            }

            ScheduleRetry();
        }

        private void ScheduleRetry()
        {
            Task.Delay(500).ContinueWith(_ =>
            {
                _ = TrySendModeAsync();
            });
        }

        public void Dispose()
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            Unsubscribe();
        }
    }
}
