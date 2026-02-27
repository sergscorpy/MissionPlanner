using System;
using System.Text;
using System.Windows.Forms;
using MissionPlanner.Controls;

namespace MissionPlanner.Utilities
{
    public class ServoControllerModuleOverlayService : IDisposable
    {
        private readonly Form owner;
        private readonly MAVLinkInterface mavLinkInterface;
        private readonly EventHandler<MAVLink.MAVLinkMessage> packetHandler;

        private ServoControllerModuleOverlayForm overlayForm;
        private bool moduleDetected;
        private bool disposed;
        private int lockMask;
        private readonly ushort[] rcChannels = new ushort[18];

        public ServoControllerModuleOverlayService(Form owner, MAVLinkInterface mavLinkInterface)
        {
            this.owner = owner;
            this.mavLinkInterface = mavLinkInterface;

            packetHandler = OnPacketReceived;
            this.mavLinkInterface.OnPacketReceived += packetHandler;
        }

        private void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (disposed)
            {
                return;
            }

            var messageId = (MAVLink.MAVLINK_MSG_ID)message.msgid;
            if (messageId == MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
            {
                HandleHeartbeat(message);
                return;
            }

            if (messageId == MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_INT)
            {
                HandleNamedValueInt(message);
                return;
            }

            if (messageId == MAVLink.MAVLINK_MSG_ID.RC_CHANNELS)
            {
                HandleRcChannels(message);
            }
        }

        private void HandleHeartbeat(MAVLink.MAVLinkMessage message)
        {
            if (moduleDetected)
            {
                return;
            }

            if (message.sysid != 1 || message.compid != (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_PERIPHERAL)
            {
                return;
            }

            moduleDetected = true;
            owner.BeginInvokeIfRequired((Action)ShowOverlay);
        }

        private void HandleNamedValueInt(MAVLink.MAVLinkMessage message)
        {
            if (message.sysid != 1 || message.compid != (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_PERIPHERAL)
            {
                return;
            }

            var namedValueInt = message.ToStructure<MAVLink.mavlink_named_value_int_t>();
            var name = Encoding.ASCII.GetString(namedValueInt.name).TrimEnd('\0', ' ');
            if (!string.Equals(name, "LOCKED", StringComparison.Ordinal))
            {
                return;
            }

            lockMask = namedValueInt.value;

            owner.BeginInvokeIfRequired((Action)(() =>
            {
                if (overlayForm != null && !overlayForm.IsDisposed)
                {
                    overlayForm.UpdateLockMask(lockMask);
                }
            }));
        }

        private void HandleRcChannels(MAVLink.MAVLinkMessage message)
        {
            //if (message.sysid != 1 || message.compid != (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_PERIPHERAL)
            //{
            //    return;
            //}

            var rc = message.ToStructure<MAVLink.mavlink_rc_channels_t>();
            rcChannels[0] = rc.chan1_raw;
            rcChannels[1] = rc.chan2_raw;
            rcChannels[2] = rc.chan3_raw;
            rcChannels[3] = rc.chan4_raw;
            rcChannels[4] = rc.chan5_raw;
            rcChannels[5] = rc.chan6_raw;
            rcChannels[6] = rc.chan7_raw;
            rcChannels[7] = rc.chan8_raw;
            rcChannels[8] = rc.chan9_raw;
            rcChannels[9] = rc.chan10_raw;
            rcChannels[10] = rc.chan11_raw;
            rcChannels[11] = rc.chan12_raw;
            rcChannels[12] = rc.chan13_raw;
            rcChannels[13] = rc.chan14_raw;
            rcChannels[14] = rc.chan15_raw;
            rcChannels[15] = rc.chan16_raw;
            rcChannels[16] = rc.chan17_raw;
            rcChannels[17] = rc.chan18_raw;

            owner.BeginInvokeIfRequired((Action)(() =>
            {
                if (overlayForm != null && !overlayForm.IsDisposed)
                {
                    overlayForm.UpdateRcChannels(rcChannels);
                }
            }));
        }

        private void ShowOverlay()
        {
            if (disposed)
            {
                return;
            }

            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new ServoControllerModuleOverlayForm();
                overlayForm.UpdateLockMask(lockMask);
                overlayForm.UpdateRcChannels(rcChannels);
                overlayForm.Show(owner);
            }
            else
            {
                overlayForm.BringToFront();
                overlayForm.Activate();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            mavLinkInterface.OnPacketReceived -= packetHandler;

            owner.BeginInvokeIfRequired((Action)(() =>
            {
                if (overlayForm != null && !overlayForm.IsDisposed)
                {
                    overlayForm.Close();
                    overlayForm.Dispose();
                }
            }));
        }
    }
}