using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using log4net;
using MissionPlanner.ArduPilot.Mavlink;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Keeps FS_THR_ENABLE synchronized with the GPS fix state on HeavyShot firmware.
    /// A fresh firmware banner is required after every detected heartbeat outage.
    /// </summary>
    internal sealed class HeavyShotGpsFailsafeManager
    {
        private const string ParameterName = "FS_THR_ENABLE";
        private const int DisabledValue = 0;
        private const int RtlValue = 1;

        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan GpsStateDebounce = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BannerRetryInterval = TimeSpan.FromSeconds(3);
        private static readonly Regex HeavyShotPattern = new Regex(@"\bHeavy\s*Shot\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly ILog Log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly object sync = new object();

        private LinkState state = LinkState.NoLink;
        private MAVLinkInterface activePort;
        private byte activeSysId;
        private byte activeCompId;
        private DateTime lastObservedHeartbeat = DateTime.MinValue;
        private int consecutiveHeartbeats;
        private int bannerSubscription = -1;
        private DateTime nextBannerRequest = DateTime.MinValue;
        private int sessionGeneration;

        private bool gpsCandidateInitialized;
        private bool gpsCandidateHasFix;
        private DateTime gpsCandidateSince;
        private int? synchronizedParameterValue;
        private Task parameterSyncTask;

        public void Update(MAVLinkInterface port)
        {
            if (port == null || port.logreadmode || port.BaseStream == null || !port.BaseStream.IsOpen)
            {
                MarkLinkLost();
                return;
            }

            var mav = port.MAV;
            if (mav == null)
            {
                MarkLinkLost();
                return;
            }

            var heartbeat = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT);
            var now = DateTime.UtcNow;
            if (heartbeat == null || heartbeat.rxtime == DateTime.MinValue ||
                now - heartbeat.rxtime > HeartbeatTimeout)
            {
                MarkLinkLost();
                return;
            }

            bool requestBanner = false;
            lock (sync)
            {
                if (activePort != port || activeSysId != mav.sysid || activeCompId != mav.compid)
                    ResetForNewLinkLocked(port, mav.sysid, mav.compid);

                if (state == LinkState.NoLink)
                    ResetForNewLinkLocked(port, mav.sysid, mav.compid);

                if (heartbeat.rxtime > lastObservedHeartbeat)
                {
                    lastObservedHeartbeat = heartbeat.rxtime;
                    consecutiveHeartbeats++;
                }

                if (state == LinkState.LinkConfirming && consecutiveHeartbeats >= 2)
                {
                    state = LinkState.FirmwareRequesting;
                    EnsureBannerSubscriptionLocked();
                    nextBannerRequest = DateTime.MinValue;
                }

                if (state == LinkState.FirmwareRequesting && now >= nextBannerRequest && !port.giveComport)
                {
                    nextBannerRequest = now + BannerRetryInterval;
                    requestBanner = true;
                }

                if (state == LinkState.Active || state == LinkState.UnsupportedFirmware)
                    RemoveBannerSubscriptionLocked();
            }

            if (requestBanner)
                RequestFirmwareIdentity(port, mav.sysid, mav.compid);

            UpdateParameterIfActive(port, mav.sysid, mav.compid, mav.cs.gpsstatus, now);
        }

        private void ResetForNewLinkLocked(MAVLinkInterface port, byte sysid, byte compid)
        {
            RemoveBannerSubscriptionLocked();
            sessionGeneration++;
            activePort = port;
            activeSysId = sysid;
            activeCompId = compid;
            state = LinkState.LinkConfirming;
            lastObservedHeartbeat = DateTime.MinValue;
            consecutiveHeartbeats = 0;
            nextBannerRequest = DateTime.MinValue;
            ResetGpsStateLocked();
            Log.InfoFormat("HeavyShot GPS failsafe: confirming MAVLink heartbeat for {0}:{1}", sysid, compid);
        }

        private void MarkLinkLost()
        {
            lock (sync)
            {
                if (state == LinkState.NoLink)
                    return;

                Log.Warn("HeavyShot GPS failsafe: autopilot heartbeat lost; firmware validation invalidated");
                RemoveBannerSubscriptionLocked();
                sessionGeneration++;
                state = LinkState.NoLink;
                activePort = null;
                lastObservedHeartbeat = DateTime.MinValue;
                consecutiveHeartbeats = 0;
                ResetGpsStateLocked();
            }
        }

        private void EnsureBannerSubscriptionLocked()
        {
            if (bannerSubscription >= 0 || activePort == null)
                return;

            int generation = sessionGeneration;
            bannerSubscription = activePort.SubscribeToPacketType(MAVLink.MAVLINK_MSG_ID.STATUSTEXT, message =>
            {
                HandleStatusText(message, generation);
                return true;
            }, activeSysId, activeCompId);
        }

        private void RemoveBannerSubscriptionLocked()
        {
            if (bannerSubscription < 0 || activePort == null)
            {
                bannerSubscription = -1;
                return;
            }

            activePort.UnSubscribeToPacketType(bannerSubscription);
            bannerSubscription = -1;
        }

        private void HandleStatusText(MAVLink.MAVLinkMessage message, int generation)
        {
            var statusText = message.ToStructure<MAVLink.mavlink_statustext_t>();
            string text = Encoding.UTF8.GetString(statusText.text).TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(text))
                return;

            bool isHeavyShot = HeavyShotPattern.IsMatch(text);
            bool isFirmwareBanner = text.IndexOf("copter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    text.IndexOf("plane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    text.IndexOf("rover", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isHeavyShot && !isFirmwareBanner)
                return;

            lock (sync)
            {
                if (generation != sessionGeneration || state != LinkState.FirmwareRequesting)
                    return;

                if (isHeavyShot)
                {
                    state = LinkState.Active;
                    ResetGpsStateLocked();
                    Log.Info("HeavyShot GPS failsafe: supported firmware confirmed: " + text);
                }
                else
                {
                    state = LinkState.UnsupportedFirmware;
                    ResetGpsStateLocked();
                    Log.Info("HeavyShot GPS failsafe: firmware is not supported: " + text);
                }
            }
        }

        private static void RequestFirmwareIdentity(MAVLinkInterface port, byte sysid, byte compid)
        {
            try
            {
                port.getVersion(sysid, compid, false);
                port.doCommand(sysid, compid, MAVLink.MAV_CMD.DO_SEND_BANNER,
                    0, 0, 0, 0, 0, 0, 0, false);
            }
            catch (Exception ex)
            {
                Log.Warn("HeavyShot GPS failsafe: firmware identity request failed", ex);
            }
        }

        private void UpdateParameterIfActive(MAVLinkInterface port, byte sysid, byte compid, float gpsStatus,
            DateTime now)
        {
            if (port.giveComport)
                return;

            int generation;
            int desiredValue;

            lock (sync)
            {
                if (state != LinkState.Active)
                    return;

                bool hasFix = gpsStatus >= (byte)MAVLink.GPS_FIX_TYPE._3D_FIX;
                if (!gpsCandidateInitialized || gpsCandidateHasFix != hasFix)
                {
                    gpsCandidateInitialized = true;
                    gpsCandidateHasFix = hasFix;
                    gpsCandidateSince = now;
                    return;
                }

                if (now - gpsCandidateSince < GpsStateDebounce)
                    return;

                desiredValue = hasFix ? RtlValue : DisabledValue;
                if (synchronizedParameterValue == desiredValue ||
                    (parameterSyncTask != null && !parameterSyncTask.IsCompleted))
                    return;

                generation = sessionGeneration;
                parameterSyncTask = SynchronizeParameterAsync(port, sysid, compid, desiredValue, generation);
            }
        }

        private async Task SynchronizeParameterAsync(MAVLinkInterface port, byte sysid, byte compid,
            int desiredValue, int generation)
        {
            try
            {
                float currentValue = await port.GetParamAsync(sysid, compid, ParameterName).ConfigureAwait(false);
                if (!CanWrite(port, sysid, compid, generation))
                    return;

                if (Math.Abs(currentValue - desiredValue) > 0.001f)
                {
                    bool success = await port.setParamAsync(sysid, compid, ParameterName, desiredValue)
                        .ConfigureAwait(false);
                    if (!success)
                    {
                        Log.WarnFormat("HeavyShot GPS failsafe: failed to set {0}={1}", ParameterName,
                            desiredValue);
                        return;
                    }
                }

                lock (sync)
                {
                    if (generation == sessionGeneration && state == LinkState.Active)
                        synchronizedParameterValue = desiredValue;
                }

                Log.InfoFormat("HeavyShot GPS failsafe: {0} synchronized to {1}", ParameterName, desiredValue);
            }
            catch (Exception ex)
            {
                Log.Warn("HeavyShot GPS failsafe: parameter synchronization failed", ex);
            }
        }

        private bool CanWrite(MAVLinkInterface port, byte sysid, byte compid, int generation)
        {
            lock (sync)
            {
                return generation == sessionGeneration && state == LinkState.Active && activePort == port &&
                       activeSysId == sysid && activeCompId == compid;
            }
        }

        private void ResetGpsStateLocked()
        {
            gpsCandidateInitialized = false;
            gpsCandidateHasFix = false;
            gpsCandidateSince = DateTime.MinValue;
            synchronizedParameterValue = null;
            parameterSyncTask = null;
        }

        private enum LinkState
        {
            NoLink,
            LinkConfirming,
            FirmwareRequesting,
            UnsupportedFirmware,
            Active
        }
    }
}
