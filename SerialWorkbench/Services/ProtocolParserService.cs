using SerialWorkbench.Models;

namespace SerialWorkbench.Services;

public sealed class ProtocolParserService
{
    private const byte PacketHeader = 0xAA;

    public ProtocolPacket Parse(SerialDataFrame frame)
    {
        var payload = frame.Payload;
        if (payload.Length < 5)
        {
            return CreateInvalidPacket(frame, "数据长度不足，无法构成协议帧。");
        }

        var header = payload[0];
        var command = payload[1];
        var length = payload[2];
        var expectedLength = length + 5;
        if (payload.Length < expectedLength)
        {
            return CreateInvalidPacket(frame, "长度字段与实际数据不匹配。");
        }

        var data = payload.Skip(3).Take(length).ToArray();
        var checksum = payload[3 + length];
        var calculatedChecksum = (byte)(header ^ command ^ length ^ data.Aggregate(0, (sum, value) => sum ^ value));
        var valid = header == PacketHeader && checksum == calculatedChecksum;

        return new ProtocolPacket
        {
            Timestamp = frame.Timestamp,
            Header = header,
            Command = command,
            Length = length,
            Data = data,
            Checksum = checksum,
            IsValid = valid,
            NumericValue = TryParseNumericValue(data),
            Description = valid
                ? $"命令 0x{command:X2} 解析成功"
                : $"命令 0x{command:X2} 校验失败"
        };
    }

    private static ProtocolPacket CreateInvalidPacket(SerialDataFrame frame, string message)
    {
        return new ProtocolPacket
        {
            Timestamp = frame.Timestamp,
            Data = frame.Payload,
            Description = message,
            IsValid = false
        };
    }

    private static double? TryParseNumericValue(byte[] data)
    {
        if (data.Length >= 2)
        {
            return BitConverter.ToUInt16(data, 0);
        }

        if (data.Length == 1)
        {
            return data[0];
        }

        return null;
    }
}
