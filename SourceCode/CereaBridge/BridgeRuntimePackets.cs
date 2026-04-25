using System;
using System.Globalization;
using System.Reflection;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
        private void SendSteerModulePacket(double actualAngleDeg)
        {
            var steerAngle100 = (short)Math.Round(Math.Clamp(actualAngleDeg * 100.0, short.MinValue, short.MaxValue));
            var bytes = new byte[14];
            bytes[0] = 0x80;
            bytes[1] = 0x81;
            bytes[2] = 0x7F;
            bytes[3] = 0xFD;
            bytes[4] = 8;
            WriteInt16(bytes, 5, steerAngle100);
            WriteInt16(bytes, 7, _imuHeading16);
            WriteInt16(bytes, 9, _imuRoll16);

            byte switches = 0;
            if (_cfg.WorkSwitchOn)
            {
                switches |= 0x01;
            }
            if (_cfg.SteerSwitchOn)
            {
                switches |= 0x02;
            }
            bytes[11] = switches;
            bytes[12] = (byte)Math.Clamp(_lastPwm, 0, 255);
            bytes[13] = 0xCC;

            _sender.Send(bytes, bytes.Length, _agioEndpoint);
        }

        private void SendAutoSteerHelloPacket(double actualAngleDeg, short rawWasCounts)
        {
            var steerAngle100 = (short)Math.Round(Math.Clamp(actualAngleDeg * 100.0, short.MinValue, short.MaxValue));
            var bytes = new byte[11];
            bytes[0] = 0x80;
            bytes[1] = 0x81;
            bytes[2] = 126;
            bytes[3] = 126;
            bytes[4] = 5;
            WriteInt16(bytes, 5, steerAngle100);
            WriteInt16(bytes, 7, rawWasCounts);

            byte switches = 0;
            if (_cfg.WorkSwitchOn)
            {
                switches |= 0x01;
            }
            if (_cfg.SteerSwitchOn)
            {
                switches |= 0x02;
            }
            bytes[9] = switches;
            bytes[10] = 0xCC;

            _sender.Send(bytes, bytes.Length, _agioEndpoint);
        }

        private static short ReadInt16(byte[] bytes, int lowIndex)
        {
            if (bytes.Length <= lowIndex + 1)
            {
                return 0;
            }

            return unchecked((short)(bytes[lowIndex] | (bytes[lowIndex + 1] << 8)));
        }

        private static ushort ReadUInt16(byte[] bytes, int lowIndex)
        {
            if (bytes.Length <= lowIndex + 1)
            {
                return 0;
            }

            return (ushort)(bytes[lowIndex] | (bytes[lowIndex + 1] << 8));
        }

        private static void WriteInt16(byte[] bytes, int lowIndex, short value)
        {
            bytes[lowIndex] = (byte)(value & 0xFF);
            bytes[lowIndex + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static short ConvertToInt16(object? value)
        {
            if (value == null)
            {
                return 0;
            }

            return Convert.ToInt16(value, CultureInfo.InvariantCulture);
        }

        private static short ReadMember(object? source, params string[] names)
        {
            if (source == null)
            {
                return 0;
            }

            var type = source.GetType();
            foreach (var name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    return ConvertToInt16(property.GetValue(source));
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    return ConvertToInt16(field.GetValue(source));
                }
            }

            return 0;
        }

        private static short NormalizeHeading16(short heading)
        {
            var normalized = heading % 5760;
            if (normalized < 0)
            {
                normalized += 5760;
            }
            return (short)normalized;
        }
    }
}
