namespace SerialWorkbench.Models;

public sealed class SerialDataFrame
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Direction { get; init; } = "RX";
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public string PreviewText { get; init; } = string.Empty;
}
