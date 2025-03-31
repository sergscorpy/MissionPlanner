using System;

namespace MissionPlanner.Utilities
{
    public class FakeGpsData
    {
        public double Lat { get; set; }
        
        public double Lng { get; set; }

        public byte Sat { get; set; }

        public int WorkCounter { get; set; }

        public float Alt { get; set; }
        
        public string FakeGpsStringId { get; set; } = "Fake GPS Point";
        
        public MAVLink.mavlink_gps_input_t CreateMavlinkPacket()
        {
            var timeUSec = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000;
            const MAVLink.GPS_INPUT_IGNORE_FLAGS ignoreFlags = (MAVLink.GPS_INPUT_IGNORE_FLAGS) 249;

            var packet = new MAVLink.mavlink_gps_input_t()
            {
                time_usec = timeUSec,
                gps_id = 0,
                ignore_flags = (ushort)ignoreFlags,
                time_week_ms = 0,
                time_week = 0,
                lat = (int)(Lat * 1e7),
                lon = (int)(Lng * 1e7),
                alt = Alt,
                hdop = 1.2f,
                vdop = 1.2f,
                vn = 0,
                ve = 0,
                vd = 0,
                speed_accuracy = 0,
                horiz_accuracy = 0,
                vert_accuracy = 0,
                satellites_visible = Sat,
                yaw = 0,
                fix_type = 6
            };

            return packet;
        }
    }
}
