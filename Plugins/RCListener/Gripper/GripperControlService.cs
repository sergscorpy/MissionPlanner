using System;
using System.Collections.Generic;
using System.Timers;
using MissionPlanner;
using MissionPlanner.Utilities;
using RCListener.Config;
using RCListener.Logging;
using RCListener.Processing;

namespace RCListener.Gripper
{
    public class GripperControlService : IDisposable
    {
        private const string EnabledKey = "rc_listener_gripper_enabled";
        private const string ServoChannelKey = "rc_listener_gripper_servo_channel";
        private const string TriggerChannelKey = "rc_listener_gripper_trigger_channel";
        private const string ServoCountKey = "rc_listener_gripper_servo_count";

        private const int DefaultServoChannel = 5;
        private const int DefaultTriggerChannel = 11;
        private const int DefaultServoCount = 4;
        private const int MaxServos = 4;
        private const int DeviceCompid = 169;
        private const int GcsCompid = 190;

        private static readonly int[] LockBits = { 8, 4, 2, 1 };

        private readonly ILogger log;
        private readonly ChannelProcessor channelProcessor;
        private readonly Timer heartbeatTimer;
        private readonly object sync = new object();

        private DateTime lastHeartbeatUtc = DateTime.MinValue;
        private bool deviceAvailable;

        public GripperControlService(ILogger log, ChannelProcessor channelProcessor)
        {
            this.log = log;
            this.channelProcessor = channelProcessor;

            LoadSettings();
            UpdateChannelConfig();

            heartbeatTimer = new Timer(500);
            heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
            heartbeatTimer.AutoReset = true;
            heartbeatTimer.Start();

            AttachMavlinkHandlers();
        }

        public event Action Updated;
        public event Action<int, bool> CommandTriggered;

        public bool IsEnabled { get; private set; }

        public bool IsDeviceAvailable
        {
            get => deviceAvailable;
            private set
            {
                if (deviceAvailable == value)
                    return;

                deviceAvailable = value;
                RaiseUpdated();
            }
        }

        public int ServoSelectionChannel { get; private set; }

        public int TriggerChannel { get; private set; }

        public int ServoCount { get; private set; }

        public int SelectedServo { get; private set; }

        public int LockMask { get; private set; }

        public bool[] LockStates { get; } = new bool[MaxServos];

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled)
                return;

            IsEnabled = enabled;
            Settings.Instance[EnabledKey] = enabled.ToString();
            RaiseUpdated();
        }

        public void SetServoSelectionChannel(int channel)
        {
            channel = Clamp(channel, 1, 24);
            if (ServoSelectionChannel == channel)
                return;

            ServoSelectionChannel = channel;
            Settings.Instance[ServoChannelKey] = channel.ToString();
            UpdateChannelConfig();
            RaiseUpdated();
        }

        public void SetTriggerChannel(int channel)
        {
            channel = Clamp(channel, 1, 24);
            if (TriggerChannel == channel)
                return;

            TriggerChannel = channel;
            Settings.Instance[TriggerChannelKey] = channel.ToString();
            UpdateChannelConfig();
            RaiseUpdated();
        }

        public void SetServoCount(int count)
        {
            count = Clamp(count, 1, MaxServos);
            if (ServoCount == count)
                return;

            ServoCount = count;
            Settings.Instance[ServoCountKey] = count.ToString();
            UpdateChannelConfig();

            if (SelectedServo > ServoCount)
                SelectedServo = ServoCount;

            RaiseUpdated();
        }

        public bool TryHandleAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            if (action.StartsWith("servo_", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(action.Substring("servo_".Length), out int servo))
                {
                    SetSelectedServo(servo);
                    return true;
                }
            }

            if (string.Equals(action, "gripper_trigger", StringComparison.OrdinalIgnoreCase))
            {
                TriggerGripper();
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (heartbeatTimer != null)
            {
                heartbeatTimer.Stop();
                heartbeatTimer.Elapsed -= HeartbeatTimer_Elapsed;
                heartbeatTimer.Dispose();
            }

            DetachMavlinkHandlers();
        }

        private void LoadSettings()
        {
            IsEnabled = Settings.Instance.GetBoolean(EnabledKey, true);
            ServoSelectionChannel = Settings.Instance.GetInt32(ServoChannelKey, DefaultServoChannel);
            TriggerChannel = Settings.Instance.GetInt32(TriggerChannelKey, DefaultTriggerChannel);
            ServoCount = Settings.Instance.GetInt32(ServoCountKey, DefaultServoCount);

            ServoSelectionChannel = Clamp(ServoSelectionChannel, 1, 24);
            TriggerChannel = Clamp(TriggerChannel, 1, 24);
            ServoCount = Clamp(ServoCount, 1, MaxServos);
            SelectedServo = Clamp(SelectedServo == 0 ? 1 : SelectedServo, 1, ServoCount);
        }

        private void UpdateChannelConfig()
        {
            var config = new Dictionary<int, ChannelConfig>();

            config[ServoSelectionChannel] = new ChannelConfig
            {
                Name = "Servo control",
                Ranges = BuildServoRanges()
            };

            config[TriggerChannel] = new ChannelConfig
            {
                Name = "Gripper trigger",
                Ranges =
                {
                    new RangeAction { Min = 1700, Max = 2000, Action = "gripper_trigger" }
                }
            };

            channelProcessor.UpdateSupplementalChannelConfig(config);
        }

        private List<RangeAction> BuildServoRanges()
        {
            var ranges = new List<RangeAction>();

            if (ServoCount >= 1)
                ranges.Add(new RangeAction { Min = 999, Max = 1100, Action = "servo_1" });
            if (ServoCount >= 2)
                ranges.Add(new RangeAction { Min = 1101, Max = 1300, Action = "servo_2" });
            if (ServoCount >= 3)
                ranges.Add(new RangeAction { Min = 1301, Max = 1500, Action = "servo_3" });
            if (ServoCount >= 4)
                ranges.Add(new RangeAction { Min = 1501, Max = 1700, Action = "servo_4" });

            return ranges;
        }

        private void SetSelectedServo(int servo)
        {
            servo = Clamp(servo, 1, ServoCount);

            if (SelectedServo == servo)
                return;

            SelectedServo = servo;
            RaiseUpdated();
        }

        private void TriggerGripper()
        {
            if (!IsEnabled || !IsDeviceAvailable)
                return;

            int servo = Clamp(SelectedServo, 1, ServoCount);
            if (servo == 0)
                return;

            var port = MainV2.comPort;
            if (port?.BaseStream?.IsOpen != true)
                return;

            try
            {
                port.doCommand(
                    (byte)port.sysidcurrent,
                    (byte)DeviceCompid,
                    MAVLink.MAV_CMD.DO_GRIPPER,
                    servo,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    requireack: false);

                log?.Log($"[GRIPPER] Command sent to servo {servo}");
                bool isLocked = false;
                lock (sync)
                {
                    if (servo > 0 && servo <= LockStates.Length)
                        isLocked = LockStates[servo - 1];
                }
                RaiseCommandTriggered(servo, isLocked);
            }
            catch (Exception ex)
            {
                log?.Log($"[GRIPPER] Send command error: {ex.Message}");
            }
        }

        private void AttachMavlinkHandlers()
        {
            if (MainV2.comPort == null)
                return;

            MainV2.comPort.OnPacketReceived += OnPacketReceived;
        }

        private void DetachMavlinkHandlers()
        {
            if (MainV2.comPort == null)
                return;

            MainV2.comPort.OnPacketReceived -= OnPacketReceived;
        }

        private void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (message == null)
                return;

            if (message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
            {
                if (message.compid == DeviceCompid)
                {
                    lock (sync)
                    {
                        lastHeartbeatUtc = DateTime.UtcNow;
                    }
                    IsDeviceAvailable = true;
                }
                return;
            }

            if (message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG)
                return;

            var cmd = message.ToStructure<MAVLink.mavlink_command_long_t>();

            if (cmd.command != (ushort)MAVLink.MAV_CMD.DO_GRIPPER)
                return;

            if (message.compid != DeviceCompid)
                return;

            if (cmd.target_component != GcsCompid && cmd.target_component != 0)
                return;

            int newMask = (int)Math.Round(cmd.param3);
            UpdateLockMask(newMask);
        }

        private void UpdateLockMask(int mask)
        {
            bool changed = false;

            lock (sync)
            {
                if (LockMask == mask)
                    return;

                LockMask = mask;

                for (int i = 0; i < LockStates.Length; i++)
                {
                    bool locked = (mask & LockBits[i]) != 0;
                    if (LockStates[i] != locked)
                    {
                        LockStates[i] = locked;
                        changed = true;
                    }
                }
            }

            if (changed)
                RaiseUpdated();
        }

        private void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DateTime lastHeartbeat;
            lock (sync)
            {
                lastHeartbeat = lastHeartbeatUtc;
            }

            bool available = lastHeartbeat != DateTime.MinValue &&
                             (DateTime.UtcNow - lastHeartbeat).TotalSeconds < 2.5;

            IsDeviceAvailable = available;
        }

        private void RaiseUpdated()
        {
            try
            {
                Updated?.Invoke();
            }
            catch
            {
            }
        }

        private void RaiseCommandTriggered(int servo, bool isLocked)
        {
            try
            {
                CommandTriggered?.Invoke(servo, isLocked);
            }
            catch
            {
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}