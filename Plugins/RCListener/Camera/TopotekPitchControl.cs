using System;
using System.Globalization;
using System.Text;

namespace RCListener.Camera
{
    // State belongs to one active RC connection. All angles are in hundredths of a degree.
    public sealed class TopotekPitchControl
    {
        public const int Channel = 24;
        private const int MinAngle = -9000;
        private const int MaxAngle = 5000;
        private const int ChangeThreshold = 50;
        private const int SendIntervalMs = 100;
        private const int SettleTimeMs = 150;
        private const int Speed = 99; // 9.9 degrees/second, per the GAP protocol.

        private int? observedAngle;
        private int? referenceAngle;
        private long lastChangeMs;
        private long? lastSendMs;

        public void Reset()
        {
            observedAngle = null;
            referenceAngle = null;
            lastSendMs = null;
            lastChangeMs = 0;
        }

        // True reserves pitch for CH24 in this frame, including while rate-limited.
        // A later CH19/CH21 action cancels an unsent target if CH24 is stationary.
        public bool Process(ushort[] channels, long nowMs, bool manualPitchAction, out byte[] command)
        {
            command = null;
            int raw = channels != null && channels.Length >= Channel ? channels[Channel - 1] : 0;
            // Accept the RadioMaster's native endpoints; reject absent/malformed values
            // before ChannelProcessor clamps extra channels to 1000..2000.
            if (raw < 988 || raw > 2012)
            {
                observedAngle = null;
                referenceAngle = null;
                return false;
            }

            int pwm = Math.Max(1000, Math.Min(2000, raw));
            int angle = MinAngle + (pwm - 1000) * (MaxAngle - MinAngle) / 1000;
            bool moved = observedAngle != angle;
            if (moved)
                lastChangeMs = nowMs;
            observedAngle = angle;

            bool needsCommand = !referenceAngle.HasValue ||
                Math.Abs(angle - referenceAngle.Value) >= ChangeThreshold ||
                (angle != referenceAngle.Value && nowMs - lastChangeMs >= SettleTimeMs);

            if (manualPitchAction && (!moved || !needsCommand))
            {
                referenceAngle = angle;
                return false;
            }

            if (!needsCommand)
                return false;

            if (lastSendMs.HasValue && nowMs - lastSendMs.Value < SendIntervalMs)
                return true;

            command = BuildCommand(angle);
            referenceAngle = angle;
            lastSendMs = nowMs;
            return true;
        }

        public static bool IsPitchAction(string action)
        {
            return action == "down" || action == "center" ||
                   (action != null && action.StartsWith("pitch_", StringComparison.Ordinal));
        }

        public static byte[] BuildCommand(int angleHundredths)
        {
            int angle = Math.Max(MinAngle, Math.Min(MaxAngle, angleHundredths));
            string body = "#tpUG6wGAP" + unchecked((ushort)(short)angle).ToString("X4", CultureInfo.InvariantCulture)
                + Speed.ToString("X2", CultureInfo.InvariantCulture);
            // TOPOTEK checksum is the low byte of the ASCII sum, including the header.
            int checksum = 0;
            foreach (char character in body)
                checksum += character;
            return Encoding.ASCII.GetBytes(body + (checksum & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
        }
    }
}
