using System;
using RCListener.Camera;
using RCListener.Gripper;
using RCListener.Logging;

namespace RCListener
{
    public static class RcListenerContext
    {
        public static CameraSelectionService CameraSelection { get; set; }
        public static GripperControlService GripperControl { get; set; }
        public static ILogger Logger { get; set; }
    }
}