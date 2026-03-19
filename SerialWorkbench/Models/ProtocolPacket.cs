namespace SerialWorkbench.Models;

public sealed class ProtocolPacket
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public byte Header { get; init; }
    public string HeaderHex => $"0x{Header:X2}";
    public byte Command { get; init; }
    public string CommandHex => $"0x{Command:X2}";
    public byte Length { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string DataHex => Convert.ToHexString(Data);
    public byte Checksum { get; init; }
    public bool IsValid { get; init; }
    public double? NumericValue { get; init; }
    public string Description { get; init; } = string.Empty;
}
