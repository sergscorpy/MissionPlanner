using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RCListener.Camera;
using RCListener.Config;
using RCListener.Logging;
using RCListener.Model;
using RCListener.Processing;
using RCListener.Transport;

internal static class TopotekPitchTests
{
    private static int checks;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }

    private static ushort[] Channels(ushort pwm)
    {
        var channels = Enumerable.Repeat((ushort)1500, 24).ToArray();
        channels[23] = pwm;
        return channels;
    }

    private static string Text(byte[] command) => command == null ? null : Encoding.ASCII.GetString(command);

    private static void MappingAndValidation()
    {
        foreach (var sample in new[] {
            Tuple.Create((ushort)988, "#tpUG6wGAPDCD86394"),
            Tuple.Create((ushort)1000, "#tpUG6wGAPDCD86394"),
            Tuple.Create((ushort)1500, "#tpUG6wGAPF8306372"),
            Tuple.Create((ushort)2000, "#tpUG6wGAP13886365"),
            Tuple.Create((ushort)2012, "#tpUG6wGAP13886365") })
        {
            var control = new TopotekPitchControl();
            byte[] command;
            Check(control.Process(Channels(sample.Item1), 0, false, out command), "Initial target takes priority");
            Check(Text(command) == sample.Item2, "PWM mapping, signed encoding and checksum: " + sample.Item1);
            control.Process(Channels(sample.Item1), 1000, false, out command);
            Check(command == null, "Stationary input does not repeat");
        }

        Check(Text(TopotekPitchControl.BuildCommand(-4500)) == "#tpUG6wGAPEE6C6394", "Negative angle packet");
        Check(Text(TopotekPitchControl.BuildCommand(int.MinValue)) == "#tpUG6wGAPDCD86394", "Lower angle clamp");
        Check(Text(TopotekPitchControl.BuildCommand(int.MaxValue)) == "#tpUG6wGAP13886365", "Upper angle clamp");
        foreach (var channels in new[] { null, new ushort[23], Channels(0), Channels(987), Channels(2013), Channels(65535) })
        {
            byte[] command;
            Check(!new TopotekPitchControl().Process(channels, 0, false, out command) && command == null,
                "Missing/invalid CH24 is ignored");
        }

        var parser = new RcFrameParser(new SilentLogger());
        var frame = parser.Push("$RM," + string.Join(",", Enumerable.Repeat("1500", 23)) + "\n").Single();
        new ChannelProcessor().Process(frame);
        byte[] ignored;
        Check(!new TopotekPitchControl().Process(frame.Channels, 0, false, out ignored),
            "Short serial frame stays invalid even after normal channel processing");
    }

    private static void TimingAndManualControl()
    {
        var control = new TopotekPitchControl();
        byte[] command;
        control.Process(Channels(1000), 0, false, out command);
        Check(control.Process(Channels(1500), 20, false, out command) && command == null, "Rate-limited target owns pitch");
        control.Process(Channels(2000), 100, false, out command);
        Check(Text(command) == "#tpUG6wGAP13886365", "Only latest rate-limited target is sent");

        control.Process(Channels(1999), 120, false, out command);
        Check(command == null, "Sub-threshold movement is held");
        control.Process(Channels(1999), 260, false, out command);
        Check(command == null, "Wait for settled input");
        control.Process(Channels(1999), 280, false, out command);
        Check(command != null, "Final sub-threshold target is delivered");

        control.Reset();
        Check(control.Process(Channels(1500), 300, true, out command) && command != null,
            "First valid CH24 wins simultaneous manual pitch action");
        Check(!control.Process(Channels(1500), 320, true, out command), "Stationary CH24 yields to manual action");
        control.Process(Channels(1500), 1000, false, out command);
        Check(command == null, "Manual action is not undone by stationary CH24");
        control.Process(Channels(1600), 1020, false, out command);
        Check(command != null, "Moving CH24 resumes angle control");
        control.Process(Channels(1700), 1040, false, out command);
        Check(command == null, "Pending target is rate-limited");
        Check(!control.Process(Channels(1700), 1060, true, out command), "Later manual action cancels pending target");
        control.Process(Channels(1700), 1500, false, out command);
        Check(command == null, "Cancelled target never overrides manual control later");

        control.Reset();
        long lastSend = -100;
        int sends = 0;
        for (long ms = 0; ms <= 2000; ms += 20)
        {
            control.Process(Channels((ushort)(1000 + ms / 2)), ms, false, out command);
            if (command == null) continue;
            Check(ms - lastSend >= 100, "Continuous movement respects 10 Hz limit");
            lastSend = ms;
            sends++;
        }
        Check(sends == 21, "Continuous movement is sampled at 10 Hz");
        control.Reset();
        control.Process(Channels(1500), 2100, false, out command);
        Check(command != null, "Reconnect/reset sends initial target again");
    }

    private static string Receive(UdpClient receiver)
    {
        IPEndPoint source = null;
        return Encoding.ASCII.GetString(receiver.Receive(ref source));
    }

    private static void NoPacket(UdpClient receiver, string name)
    {
        try { Receive(receiver); throw new Exception(name); }
        catch (SocketException ex)
        {
            Check(ex.SocketErrorCode == SocketError.TimedOut, name);
        }
    }

    private static void SenderIntegration()
    {
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var sender = new GimbalCommandSender(new SilentLogger()))
        {
            receiver.Client.ReceiveTimeout = 150;
            int port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;
            var commands = new Dictionary<string, byte[]> {
                ["center"] = Encoding.ASCII.GetBytes("CENTER"),
                ["pitch_stop"] = Encoding.ASCII.GetBytes("STOP"),
                ["zoom_in"] = Encoding.ASCII.GetBytes("ZOOM") };
            var channels = new Dictionary<int, ChannelConfig>();
            var frame = new RcFrame(Channels(1500));
            sender.SetProfile(new CameraProfile(CameraProfiles.TopotekCameraName, "127.0.0.1", port, false, channels, commands));
            sender.SendFrameActions(frame, new[] { "center", "pitch_stop", "zoom_in" });
            Check(Receive(receiver) == "ZOOM", "CH24 priority preserves unrelated camera actions");
            Check(Receive(receiver) == "#tpUG6wGAPF8306372", "TOPOTEK emits GAP instead of conflicting pitch actions");
            NoPacket(receiver, "No conflicting pitch packet follows GAP");
            sender.SendFrameActions(frame, new[] { "center" });
            Check(Receive(receiver) == "CENTER", "Stationary CH24 allows CH19");
            sender.SendFrameActions(frame, new string[0]);
            NoPacket(receiver, "Stationary CH24 does not undo center");

            sender.SetProfile(new CameraProfile(CameraProfiles.DefaultCameraName, "127.0.0.1", port, false, channels, commands));
            sender.SendFrameActions(new RcFrame(Channels(2000)), new[] { "pitch_stop" });
            Check(Receive(receiver) == "STOP", "SiYi keeps its existing actions");
            NoPacket(receiver, "SiYi ignores CH24");
            sender.SetProfile(new CameraProfile(CameraProfiles.TopotekCameraName, "127.0.0.1", port, false, channels, commands));
            sender.SendFrameActions(frame, new string[0]);
            Check(Receive(receiver) == "#tpUG6wGAPF8306372", "Selecting TOPOTEK reapplies current CH24");
            sender.ResetPitchControl();
            sender.SendFrameActions(frame, new string[0]);
            Check(Receive(receiver) == "#tpUG6wGAPF8306372", "Reconnect resets sender pitch state");
        }
    }

    public static int Main()
    {
        try
        {
            MappingAndValidation();
            TimingAndManualControl();
            SenderIntegration();
            Console.WriteLine("PASS: " + checks + " pitch checks (including loopback UDP).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Log(string message) { }
    }
}
