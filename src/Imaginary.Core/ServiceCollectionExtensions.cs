using Imaginary.Core.Logging;
using Imaginary.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Imaginary.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImaginaryCore(this IServiceCollection services)
    {
        services.AddSingleton<ILogService>(LogService.Default);
        services.AddSingleton<IImageFormatDetector, ImageFormatDetector>();
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IColorQuantizer, OctreeQuantizer>();
        services.AddSingleton<IWatermarkService, WatermarkService>();
        services.AddSingleton<IIcoEncoder, IcoEncoder>();
        services.AddSingleton<IImageConverter, ImageConverter>();
        services.AddSingleton<ISizeConstraintSolver, SizeConstraintSolver>();
        services.AddSingleton<IOutputPathResolver, OutputPathResolver>();
        services.AddSingleton<IPresetManager, PresetManager>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IExplorerIntegration, ExplorerIntegration>();
        services.AddSingleton<IAutostartService, AutostartService>();
        services.AddSingleton<IHotfolderWatcher, HotfolderWatcher>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IImageEditorService, ImageEditorService>();
        services.AddSingleton<IBackgroundRemovalService, BackgroundRemovalService>();
        services.AddTransient<IBatchProcessor, BatchProcessor>();
        return services;
    }
}
