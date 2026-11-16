using System;
using System.Collections.Generic;
using System.Linq;
using RCListener.Logging;
using RCListener.Model;

namespace RCListener.Processing
{
    public class RcFrameParser
    {
        private readonly ILogger _log;
        private string _buffer = string.Empty;

        public RcFrameParser(ILogger log)
        {
            _log = log;
        }

        public IEnumerable<RcFrame> Push(string data)
        {
            if (string.IsNullOrEmpty(data))
                yield break;

            _buffer += data;
            var lines = _buffer.Split('\n');
            _buffer = lines.Last();

            foreach (var raw in lines.Take(lines.Length - 1))
            {
                var line = raw.Trim();
                if (!line.StartsWith("$RM,", StringComparison.Ordinal))
                    continue;

                var payload = line.Substring(4).Split(',');
                var count = Math.Min(24, payload.Length);
                var channels = new ushort[24];

                for (int i = 0; i < count; i++)
                {
                    if (ushort.TryParse(payload[i], out var value))
                        channels[i] = value;
                    else
                        channels[i] = 0;
                }

                yield return new RcFrame(channels);
            }
        }
    }
}