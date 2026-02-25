using System;
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

        public ServoControllerModuleOverlayService(Form owner, MAVLinkInterface mavLinkInterface)
        {
            this.owner = owner;
            this.mavLinkInterface = mavLinkInterface;

            packetHandler = OnPacketReceived;
            this.mavLinkInterface.OnPacketReceived += packetHandler;
        }

        private void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (disposed || moduleDetected)
            {
                return;
            }

            if ((MAVLink.MAVLINK_MSG_ID)message.msgid != MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
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

        private void ShowOverlay()
        {
            if (disposed)
            {
                return;
            }

            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new ServoControllerModuleOverlayForm();
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