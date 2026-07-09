using System;

namespace MissionPlanner.GCSViews.SituationMarkers
{
    public enum SituationMarkerAltitudeSource
    {
        Srtm,
        Manual
    }

    public class SituationMarker
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public double? Altitude { get; set; }
        public bool IsHome { get; set; }
        public bool IsInterest { get; set; }
        public SituationMarkerAltitudeSource AltitudeSource { get; set; } = SituationMarkerAltitudeSource.Srtm;

        public bool HasValidPosition
        {
            get
            {
                return Lat.HasValue && Lng.HasValue &&
                       Math.Abs(Lat.Value) <= 90 &&
                       Math.Abs(Lng.Value) <= 180 &&
                       !(Lat.Value == 0 && Lng.Value == 0);
            }
        }
    }
}

