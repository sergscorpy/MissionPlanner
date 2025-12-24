using MissionPlanner;
using MissionPlanner.Plugin;
using System;
using System.IO;
using RCListener.Control;
using RCListener.Processing;
using RCListener.Transport;
using RCListener.Ui;

namespace RCListener
{
    public class RCListener : Plugin
    {
        private readonly RcListenerController controller;
        private readonly UiStatusPresenter statusPresenter;
        private readonly string lastPortCacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rc_listener_last_port.txt");
        private bool running;

        public override string Name => "RadioMaster RC Control";
        public override string Version => "1.6";
        public override string Author => "fortis";

        public RCListener()
        {
            var frameParser = new RcFrameParser(Log);
            var channelProcessor = new ChannelProcessor();
            var gimbalSender = new GimbalCommandSender(Log);
            var serialSession = new SerialSession(Log);
            var portScanner = new PortScanner(Log, lastPortCacheFile);

            controller = new RcListenerController(Log, serialSession, portScanner, frameParser, channelProcessor, gimbalSender);
            statusPresenter = new UiStatusPresenter(Log, () => controller.RequestManualRescan());

            controller.ConnectionChanged += statusPresenter.SetConnected;
            controller.ScanStateChanged += statusPresenter.SetScanning;
        }

        public override bool Init() => true;

        public override bool Loaded()
        {
            Log("RC Control plugin loaded");
            running = true;

            statusPresenter.Initialize();
            controller.Start();

            if (Host != null)
            {
                try { Host.DeviceChanged += OnDeviceChanged; } catch { }
            }

            return true;
        }

        private void OnDeviceChanged(MainV2.WM_DEVICECHANGE_enum cause)
        {
            if (!running)
                return;

            controller.HandleDeviceChange();
        }

        public override bool Loop()
        {
            loopratehz = 0.5f;
            return true;
        }

        public override bool Exit()
        {
            try
            {
                Log("[EXIT] Stopping RC Control plugin...");
                running = false;

                if (Host != null)
                {
                    try { Host.DeviceChanged -= OnDeviceChanged; } catch { }
                }

                try
                {
                    controller.ConnectionChanged -= statusPresenter.SetConnected;
                    controller.ScanStateChanged -= statusPresenter.SetScanning;
                }
                catch { }

                controller.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                try
                {
                    statusPresenter.SetConnected(false);
                }
                catch { }

                try
                {
                    statusPresenter.Dispose();
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] UI dispose error: {ex.Message}");
                }

                Log("[EXIT] RC Control stopped cleanly");
            }
            catch (Exception ex)
            {
                Log($"[EXIT] Unexpected error: {ex.Message}");
            }

            return true;
        }

        private void Log(string msg)
        {
            try { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"); }
            catch { System.Diagnostics.Debug.WriteLine(msg); }
        }
    }
}
