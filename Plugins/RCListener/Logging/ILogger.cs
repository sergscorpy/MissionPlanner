using System;

namespace RCListener.Logging
{
    public interface ILogger
    {
        void Log(string message);
    }

    public class TimestampedLogger : ILogger
    {
        private readonly string prefix;

        public TimestampedLogger(string prefix = "[RCListener]")
        {
            this.prefix = prefix;
        }

        public void Log(string message)
        {
            var formatted = $"{prefix} {message}";

            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {formatted}");
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(formatted);
            }
        }
    }
}