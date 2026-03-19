using SerialWorkbench.Models;

namespace SerialWorkbench.Services;

public sealed class ReplayService
{
    public async Task ReplayAsync(IEnumerable<SerialDataFrame> frames, Func<SerialDataFrame, Task> onFrame, bool keepOriginalInterval, CancellationToken cancellationToken)
    {
        SerialDataFrame? previous = null;
        foreach (var frame in frames.OrderBy(item => item.Timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (keepOriginalInterval && previous is not null)
            {
                var delay = frame.Timestamp - previous.Timestamp;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            await onFrame(frame);
            previous = frame;
        }
    }
}
