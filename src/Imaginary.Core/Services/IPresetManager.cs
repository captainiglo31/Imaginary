using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface IPresetManager
{
    IReadOnlyList<Preset> GetAllPresets();
    Preset? GetPreset(string id);
    void SaveUserPreset(Preset preset);
    bool DeleteUserPreset(string id);
}
