using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface IBatchProcessor
{
    Task<ImageJobResult> ProcessSingleAsync(
        ImageJobInput input,
        ConversionOptions options,
        bool saveToDisk = true,
        CancellationToken cancellationToken = default);

    Task<BatchResult> ProcessBatchAsync(
        IReadOnlyList<ImageJobInput> inputs,
        ConversionOptions options,
        bool saveToDisk = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
