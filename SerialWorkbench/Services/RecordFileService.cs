using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SerialWorkbench.Models;

namespace SerialWorkbench.Services;

public sealed class RecordFileService
{
    public async Task SaveFramesAsync(IEnumerable<SerialDataFrame> frames, string filePath, CancellationToken cancellationToken = default)
    {
        var lines = frames.Select(frame => $"{frame.Timestamp:O}|{frame.Direction}|{Convert.ToHexString(frame.Payload)}|{frame.PreviewText}");
        await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8, cancellationToken);
    }

    public async Task<IReadOnlyList<SerialDataFrame>> LoadFramesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var results = new List<SerialDataFrame>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 4)
            {
                continue;
            }

            results.Add(new SerialDataFrame
            {
                Timestamp = DateTime.TryParse(parts[0], out var timestamp) ? timestamp : DateTime.Now,
                Direction = parts[1],
                Payload = Convert.FromHexString(parts[2]),
                PreviewText = parts[3]
            });
        }

        return results;
    }
}
