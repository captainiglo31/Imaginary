using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }
    Task<UpdateInfo> CheckForUpdateAsync(string owner, string repo, CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    bool ApplyUpdateAndRestart(string downloadedFilePath, string? targetExecutablePath = null);
}
