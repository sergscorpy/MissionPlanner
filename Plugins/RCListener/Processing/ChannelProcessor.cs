using System;
using System.Collections.Generic;
using RCListener.Config;
using RCListener.Model;

namespace RCListener.Processing
{
    public class ChannelProcessor
    {
        private const double EmaAlpha = 0.35;
        private readonly double[] _ema = new double[18];
        private readonly ushort[] _latestChannels = new ushort[24];
        private readonly Dictionary<int, string> _lastRangeAction = new Dictionary<int, string>();
        private readonly object _sync = new object();

        public ushort[] SnapshotChannels()
        {
            lock (_sync)
            {
                var copy = new ushort[_latestChannels.Length];
                Array.Copy(_latestChannels, copy, _latestChannels.Length);
                return copy;
            }
        }

        public ProcessResult Process(RcFrame frame)
        {
            var actions = new List<string>();

            lock (_sync)
            {
                for (int i = 0; i < 18; i++)
                {
                    var raw = frame.Channels[i];
                    _latestChannels[i] = raw == 0 ? (ushort)0 : NormalizePwm(raw);
                }

                for (int i = 18; i < Math.Min(frame.Channels.Length, 24); i++)
                {
                    var raw = frame.Channels[i];
                    _latestChannels[i] = (ushort)Clamp(raw, 1000, 2000);
                }

                for (int i = Math.Min(frame.Channels.Length, 24); i < 24; i++)
                    _latestChannels[i] = 0;

                for (int ch = 19; ch <= 24; ch++)
                {
                    if (!RcConfig.Channels.TryGetValue(ch, out var cfg))
                        continue;

                    int val = _latestChannels[ch - 1];
                    string matched = null;

                    foreach (var range in cfg.Ranges)
                    {
                        if (val >= range.Min && val <= range.Max)
                        {
                            matched = range.Action;
                            break;
                        }
                    }

                    if (matched == null)
                    {
                        _lastRangeAction.Remove(ch);
                        continue;
                    }

                    if (!_lastRangeAction.TryGetValue(ch, out var prev) || prev != matched)
                    {
                        _lastRangeAction[ch] = matched;
                        actions.Add(matched);
                    }
                }
            }

            return new ProcessResult(actions);
        }

        public ushort[] BuildOverrideChannels()
        {
            var snapshot = SnapshotChannels();
            var output = new ushort[18];

            for (int i = 0; i < 18; i++)
            {
                ushort raw = snapshot[i];
                if (i <= 3)
                {
                    if (raw == 0)
                    {
                        output[i] = 0;
                    }
                    else
                    {
                        if (_ema[i] == 0)
                            _ema[i] = raw;
                        _ema[i] = EmaAlpha * raw + (1 - EmaAlpha) * _ema[i];
                        output[i] = (ushort)Math.Round(_ema[i]);
                    }
                }
                else
                {
                    output[i] = raw;
                }
            }

            return output;
        }

        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        private static ushort NormalizePwm(ushort val)
        {
            const double inMin = 988.0;
            const double inMax = 2012.0;
            const double outMin = 1000.0;
            const double outMax = 2000.0;

            if (val <= inMin) return (ushort)outMin;
            if (val >= inMax) return (ushort)outMax;

            double scaled = (val - inMin) / (inMax - inMin);
            double result = outMin + scaled * (outMax - outMin);
            return (ushort)Math.Round(result);
        }
    }

    public class ProcessResult
    {
        public ProcessResult(IEnumerable<string> actions)
        {
            Actions = new List<string>(actions);
        }

        public List<string> Actions { get; }
    }
}