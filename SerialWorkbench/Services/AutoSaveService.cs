using SerialWorkbench.Models;

namespace SerialWorkbench.Services;

public sealed class AutoSaveService
{
    private readonly RecordFileService _recordFileService;
    private DateTime _lastSaveTime = DateTime.MinValue;

    public AutoSaveService(RecordFileService recordFileService)
    {
        _recordFileService = recordFileService;
    }

    public async Task TryAutoSaveAsync(IEnumerable<SerialDataFrame> frames, string folderPath, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        if (DateTime.Now - _lastSaveTime < interval)
        {
            return;
        }

        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, $"record_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        await _recordFileService.SaveFramesAsync(frames, filePath, cancellationToken);
        _lastSaveTime = DateTime.Now;
    }
}
