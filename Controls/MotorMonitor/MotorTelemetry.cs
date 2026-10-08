using System;
using System.Collections.Generic;
using System.Reflection;
using MissionPlanner.ArduPilot;

namespace MissionPlanner.Controls.MotorMonitor
{
    internal sealed class MotorReading
    {
        public int Number;
        public int Output;
        public float Command;
        public float Voltage;
        public float Current;
        public float Rpm;
    }

    internal sealed class MotorSnapshot
    {
        public MotorReading[] Motors;
        public string Warning;
    }

    internal static class MotorTelemetry
    {
        private static readonly PropertyInfo[] Outputs = Properties("ch", "out", 32);
        private static readonly PropertyInfo[] Voltages = Properties("esc", "_volt", 16);
        private static readonly PropertyInfo[] Currents = Properties("esc", "_curr", 16);
        private static readonly PropertyInfo[] Rpms = Properties("esc", "_rpm", 16);

        private static PropertyInfo[] Properties(string prefix, string suffix, int count)
        {
            var result = new PropertyInfo[count];
            for (int i = 0; i < count; i++)
                result[i] = typeof(CurrentState).GetProperty(prefix + (i + 1) + suffix);
            return result;
        }

        internal static bool Parameter(MAVState mav, string name, out float value)
        {
            value = 0;
            var parameter = mav?.param?[name];
            if (parameter == null)
                return false;
            value = (float)parameter.Value;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryLayout(MAVState mav, out MotorLayout layout, out string error)
        {
            layout = null;
            error = null;
            if (mav?.cs == null || mav.cs.firmware != Firmwares.ArduCopter2)
            {
                error = "Монітор двигунів доступний лише для Copter.";
                return false;
            }
            if (!Parameter(mav, "FRAME_CLASS", out var frameClass) || !Parameter(mav, "FRAME_TYPE", out var frameType))
            {
                error = "Спочатку завантажте параметри FRAME_CLASS та FRAME_TYPE.";
                return false;
            }
            layout = MotorLayout.Find((int)frameClass, (int)frameType);
            if (layout == null)
                error = $"Схема FRAME_CLASS={frameClass}, FRAME_TYPE={frameType} не підтримується.";
            return layout != null;
        }

        public static float CommandPercent(float output, float minimum, float maximum)
        {
            if (output <= 0 || maximum <= minimum || float.IsNaN(output) || float.IsInfinity(output))
                return 0;
            return Math.Max(0, Math.Min(100, 100 * (output - minimum) / (maximum - minimum)));
        }

        public static MotorSnapshot Read(MAVState mav, MotorLayout layout)
        {
            var warnings = new List<string>();
            bool minimumValid = Parameter(mav, "MOT_PWM_MIN", out var minimum);
            bool maximumValid = Parameter(mav, "MOT_PWM_MAX", out var maximum);
            // Legacy Copter uses calibrated throttle endpoints when MOT_PWM_* is zero.
            if (minimumValid && minimum == 0)
                minimumValid = Parameter(mav, "RC3_MIN", out minimum);
            if (maximumValid && maximum == 0)
                maximumValid = Parameter(mav, "RC3_MAX", out maximum);
            bool rangeValid = minimumValid && maximumValid && minimum > 0 && maximum > minimum;
            if (!rangeValid)
                warnings.Add("Діапазон команди двигунів недоступний або некоректний; команда показана як 0%.");

            bool offsetValid = !Parameter(mav, "ESC_TELEM_MAV_OFS", out var offset) || offset == 0;
            if (!offsetValid)
                warnings.Add("ESC_TELEM_MAV_OFS ≠ 0: показники ESC не зіставляються з моторами й показані як 0.");

            var motorOutputs = new Dictionary<int, int>();
            var duplicateMotors = new HashSet<int>();
            for (int channel = 1; channel <= 32; channel++)
            {
                if (!Parameter(mav, "SERVO" + channel + "_FUNCTION", out var function) || function < 33 || function > 40)
                    continue;
                int number = (int)function - 32;
                if (motorOutputs.ContainsKey(number))
                    duplicateMotors.Add(number);
                else
                    motorOutputs.Add(number, channel);
            }
            var readings = new List<MotorReading>();
            foreach (var motor in layout.Motors)
            {
                // Motor1..Motor8 are output functions 33..40. Never assume MotorN = SERVOn.
                motorOutputs.TryGetValue(motor.Number, out int output);
                bool duplicate = duplicateMotors.Contains(motor.Number);
                var reading = new MotorReading { Number = motor.Number, Output = output };
                if (output == 0 || duplicate)
                    warnings.Add($"M{motor.Number}: {(duplicate ? "неоднозначне" : "відсутнє")} призначення SERVOx_FUNCTION; показники показані як 0.");
                else
                {
                    reading.Command = rangeValid ? CommandPercent(Value(Outputs, output, mav.cs), minimum, maximum) : 0;
                    if (offsetValid)
                    {
                        reading.Voltage = Value(Voltages, output, mav.cs);
                        reading.Current = Value(Currents, output, mav.cs);
                        reading.Rpm = Value(Rpms, output, mav.cs);
                        if (output > Voltages.Length)
                            warnings.Add($"M{motor.Number}: для виходу {output} поля телеметрії ESC недоступні.");
                    }
                }
                readings.Add(reading);
            }
            return new MotorSnapshot { Motors = readings.ToArray(), Warning = string.Join("\n", warnings) };
        }

        private static float Value(PropertyInfo[] properties, int channel, CurrentState state)
        {
            if (channel < 1 || channel > properties.Length || properties[channel - 1] == null)
                return 0;
            var value = Convert.ToSingle(properties[channel - 1].GetValue(state));
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        }
    }
}
