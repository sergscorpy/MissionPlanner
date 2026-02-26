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