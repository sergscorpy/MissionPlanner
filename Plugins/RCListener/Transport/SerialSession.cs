using System;
using System.IO.Ports;
using System.Linq;
using RCListener.Logging;

namespace RCListener.Transport
{
    public class SerialSession : IDisposable
    {
        private readonly ILogger log;
        private readonly object portLock = new object();
        private SerialPort serialPort;
        private SerialDataReceivedEventHandler dataReceivedHandler;

        public SerialSession(ILogger log)
        {
            this.log = log;
        }

        public string ConnectedPort { get; private set; }

        public DateTime PortOpenUtc { get; private set; }

        public bool HasOpenPort => serialPort != null && serialPort.IsOpen;

        public bool TryOpen(string port, SerialDataReceivedEventHandler handler)
        {
            lock (portLock)
            {
                CloseInternal();

                try
                {
                    serialPort = new SerialPort(port, 115200)
                    {
                        ReadTimeout = 200,
                        WriteTimeout = 200
                    };

                    dataReceivedHandler = handler;
                    if (dataReceivedHandler != null)
                        serialPort.DataReceived += dataReceivedHandler;

                    serialPort.Open();

                    ConnectedPort = port;
                    PortOpenUtc = DateTime.UtcNow;
                    return true;
                }
                catch (Exception ex)
                {
                    log.Log($"[SCAN] Failed to open {port}: {ex.Message}");
                    CloseInternal();
                    return false;
                }
            }
        }

        public string ReadExisting()
        {
            lock (portLock)
            {
                if (serialPort == null)
                    return null;

                try
                {
                    return serialPort.ReadExisting();
                }
                catch (Exception ex)
                {
                    log.Log($"SerialDataReceived read error: {ex.Message}");
                    return null;
                }
            }
        }

        public bool IsHealthy(bool handshakeConfirmed, DateTime lastDataUtc, int noDataTimeoutMs)
        {
            try
            {
                lock (portLock)
                {
                    if (serialPort == null || !serialPort.IsOpen)
                        return false;
                }

                if (!handshakeConfirmed)
                    return false;

                if ((DateTime.UtcNow - lastDataUtc).TotalMilliseconds > noDataTimeoutMs)
                    return false;

                _ = serialPort.BytesToRead;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsPortStillPresent()
        {
            try
            {
                lock (portLock)
                {
                    if (serialPort == null)
                        return false;

                    _ = serialPort.BytesToRead;
                }

                var names = SerialPort.GetPortNames();
                return ConnectedPort != null && names.Contains(ConnectedPort);
            }
            catch
            {
                return false;
            }
        }

        public void Close()
        {
            lock (portLock)
            {
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            if (serialPort != null)
            {
                try
                {
                    if (dataReceivedHandler != null)
                        serialPort.DataReceived -= dataReceivedHandler;
                }
                catch
                {
                }

                try
                {
                    if (serialPort.IsOpen)
                        serialPort.Close();
                }
                catch
                {
                }

                try
                {
                    serialPort.Dispose();
                }
                catch
                {
                }

                serialPort = null;
                dataReceivedHandler = null;
            }

            ConnectedPort = null;
            PortOpenUtc = DateTime.MinValue;
        }

        public void Dispose()
        {
            Close();
        }
    }
}