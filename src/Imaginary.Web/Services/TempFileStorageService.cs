using System.Collections.Concurrent;
using System.IO.Compression;

namespace Imaginary.Web.Services;

public class StoredDownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TempFileStorageService
{
    private readonly ConcurrentDictionary<string, StoredDownloadItem> _items = new();
    private readonly string _baseDir;

    public TempFileStorageService()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "ImaginaryWeb");
        Directory.CreateDirectory(_baseDir);
    }

    public StoredDownloadItem StoreFile(string fileName, byte[] data, string contentType)
    {
        CleanupOldFiles();
        var id = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_baseDir, $"{id}_{fileName}");
        File.WriteAllBytes(filePath, data);

        var item = new StoredDownloadItem
        {
            Id = id,
            FileName = fileName,
            FilePath = filePath,
            ContentType = contentType
        };
        _items[id] = item;
        return item;
    }

    public StoredDownloadItem CreateZipArchive(string zipFileName, IEnumerable<(string FileName, byte[] Data)> files)
    {
        CleanupOldFiles();
        var id = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_baseDir, $"{id}_{zipFileName}");

        using (var zipStream = new FileStream(filePath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var entryName = file.FileName;
                var counter = 1;
                while (usedNames.Contains(entryName))
                {
                    var ext = Path.GetExtension(file.FileName);
                    var nameOnly = Path.GetFileNameWithoutExtension(file.FileName);
                    entryName = $"{nameOnly}_{counter}{ext}";
                    counter++;
                }
                usedNames.Add(entryName);

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(file.Data, 0, file.Data.Length);
            }
        }

        var item = new StoredDownloadItem
        {
            Id = id,
            FileName = zipFileName,
            FilePath = filePath,
            ContentType = "application/zip"
        };
        _items[id] = item;
        return item;
    }

    public StoredDownloadItem? GetItem(string id)
    {
        if (_items.TryGetValue(id, out var item) && File.Exists(item.FilePath))
        {
            return item;
        }
        return null;
    }

    private void CleanupOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            foreach (var kvp in _items)
            {
                if (kvp.Value.CreatedAt < cutoff)
                {
                    if (File.Exists(kvp.Value.FilePath))
                    {
                        File.Delete(kvp.Value.FilePath);
                    }
                    _items.TryRemove(kvp.Key, out _);
                }
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
