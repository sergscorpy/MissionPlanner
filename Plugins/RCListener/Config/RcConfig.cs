using System.Collections.Generic;

namespace RCListener.Config
{
    public class RangeAction
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public string Action { get; set; }
    }

    public class ChannelConfig
    {
        public string Name { get; set; } = string.Empty;
        public List<RangeAction> Ranges { get; set; } = new List<RangeAction>();
    }

}