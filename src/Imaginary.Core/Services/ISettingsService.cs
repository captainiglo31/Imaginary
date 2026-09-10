using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Reload();
}
