using Newtonsoft.Json;
using System;

namespace WeblinkPlugin.Core.Http.Models
{
    internal class ModePacket
    {
        [JsonProperty("mode")]
        public string Mode { get; set; }
    }
}
