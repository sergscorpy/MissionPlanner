using MissionPlanner;
using MissionPlanner.Plugin;
using System;
using System.IO;
using RCListener.Control;
using RCListener.Logging;
using RCListener.Processing;
using RCListener.Transport;
using RCListener.Ui;

namespace RCListener
{
    public class RCListener : Plugin
    {
        private readonly ILogger logger;
        private readonly RcListenerController controller;
        private readonly UiStatusPresenter statusPresenter;
        private readonly string lastPortCacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rc_listener_last_port.txt");
        private bool running;

        public override string Name => "RadioMaster RC Control";
        public override string Version => "1.6";
        public override string Author => "fortis";

        public RCListener()
        {
            logger = new TimestampedLogger();

            var frameParser = new RcFrameParser(logger);
            var channelProcessor = new ChannelProcessor();
            var gimbalSender = new GimbalCommandSender(logger);
            var serialSession = new SerialSession(logger);
            var portScanner = new PortScanner(logger, lastPortCacheFile);

            controller = new RcListenerController(logger, serialSession, portScanner, frameParser, channelProcessor, gimbalSender);
            statusPresenter = new UiStatusPresenter(logger, () => controller.RequestManualRescan());

            controller.ConnectionChanged += statusPresenter.SetConnected;
            controller.ScanStateChanged += statusPresenter.SetScanning;
        }

        public override bool Init() => true;

        public override bool Loaded()
        {
            logger.Log("RC Control plugin loaded");
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
                logger.Log("[EXIT] Stopping RC Control plugin...");
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
                    logger.Log($"[EXIT] UI dispose error: {ex.Message}");
                }

                logger.Log("[EXIT] RC Control stopped cleanly");
            }
            catch (Exception ex)
            {
                logger.Log($"[EXIT] Unexpected error: {ex.Message}");
            }

            return true;
        }
    }
}