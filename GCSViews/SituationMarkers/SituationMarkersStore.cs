using System;
using System.Collections.Generic;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public class SituationMarkersStore
    {
        public int FormatVersion { get; set; } = 1;
        public List<SituationMarker> Markers { get; set; } = new List<SituationMarker>();
        public Guid? InterestMarkerId { get; set; }
    }
}
