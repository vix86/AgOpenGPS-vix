using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
        private void BeginReceive(UdpClient client)
        {
            var thread = new Thread(() => ReceiveLoop(client))
            {
                IsBackground = true,
                Name = "CereaBridge UDP"
            };
            thread.Start();
        }

        private void ReceiveLoop(UdpClient client)
        {
            while (_running)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = client.Receive(ref remote);
                    HandleReceived(bytes);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (!_running)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CereaBridge receive error: " + ex.Message);
                    Thread.Sleep(20);
                }
            }
        }

        private void HandleReceived(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 5)
            {
                return;
            }

            if (bytes[0] != 0x80 || bytes[1] != 0x81)
            {
                return;
            }

            switch (bytes[3])
            {
                case 200:
                    HandleAgioHello();
                    break;
                case 0xFE:
                    ApplySteerData(bytes);
                    break;
                case 0xFC:
                    ApplySteerSettings(bytes);
                    break;
                case 0xFB:
                    ApplySteerConfig(bytes);
                    break;
            }
        }

        private void HandleAgioHello()
        {
            var actualAngleDeg = GetActualSteerAngleDegrees();
            SendAutoSteerHelloPacket(actualAngleDeg, GetRawWasCountsForHello());
            SendSteerModulePacket(actualAngleDeg);
        }

        private void ApplySteerData(byte[] bytes)
        {
            if (bytes.Length < 13)
            {
                return;
            }

            lock (_sync)
            {
                _speedKph = ReadUInt16(bytes, 5) / 10.0;
                _desiredAngleDeg = ReadInt16(bytes, 8) / 100.0;

                var status = bytes[7];
                _autosteerEnabled = (status & 0x01) != 0 || (status & 0x02) != 0;
                _lastSteerDataUtc = DateTime.UtcNow;
            }
        }

        private void ApplySteerSettings(byte[] bytes)
        {
            if (bytes.Length < 13)
            {
                return;
            }

            lock (_sync)
            {
                _kp = bytes[5];
                _highPwm = bytes[6];
                _minPwm = bytes[8];

                if (_cfg.UseAogWasCalibration)
                {
                    if (bytes[9] > 0)
                    {
                        _countsPerDegree = bytes[9];
                    }

                    _wasOffset = ReadInt16(bytes, 10);
                }
            }
        }

        private void ApplySteerConfig(byte[] bytes)
        {
            if (bytes.Length >= 8 && bytes[7] > 0)
            {
                lock (_sync)
                {
                    _minSpeedX10 = bytes[7];
                }
            }
        }
    }
}
