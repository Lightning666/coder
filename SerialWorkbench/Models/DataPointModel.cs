namespace SerialWorkbench.Models;

public sealed class DataPointModel
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public double Value { get; init; }
}
