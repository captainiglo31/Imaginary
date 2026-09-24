using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Imaginary.Core.Logging;
using Imaginary.Core.Services;

namespace Imaginary.Desktop.Services;

public class StatsReportingService : IStatsReportingService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private readonly ISettingsService _settingsService;

    public StatsReportingService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public void ReportConversion(int count, long savedBytes)
    {
        if (count <= 0) return;
        if (!_settingsService.Settings.ShareAnonymousStats) return;

        _ = Task.Run(async () =>
        {
            await SendIncrementAsync(converted: count, edited: 0, savedBytes: savedBytes);
        });
    }

    public void ReportEdit(int count = 1)
    {
        if (count <= 0) return;
        if (!_settingsService.Settings.ShareAnonymousStats) return;

        _ = Task.Run(async () =>
        {
            await SendIncrementAsync(converted: 0, edited: count, savedBytes: 0);
        });
    }

    public async Task<long?> GetGlobalCounterAsync()
    {
        try
        {
            var serverUrl = _settingsService.Settings.StatsServerUrl?.TrimEnd('/') ?? "http://localhost:5047";
            var url = $"{serverUrl}/api/stats";

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("totalActions", out var totalEl))
            {
                return totalEl.GetInt64();
            }
            if (doc.RootElement.TryGetProperty("convertedCount", out var convEl) &&
                doc.RootElement.TryGetProperty("editedCount", out var editEl))
            {
                return convEl.GetInt64() + editEl.GetInt64();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug("Stats", $"Could not fetch global counter: {ex.Message}");
        }
        return null;
    }

    private async Task SendIncrementAsync(int converted, int edited, long savedBytes)
    {
        try
        {
            var serverUrl = _settingsService.Settings.StatsServerUrl?.TrimEnd('/') ?? "http://localhost:5047";
            var url = $"{serverUrl}/api/stats/increment";

            var payload = new
            {
                converted = converted,
                edited = edited,
                savedBytes = savedBytes,
                clientVersion = "2.5.0"
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var res = await _httpClient.PostAsync(url, content);
            if (res.IsSuccessStatusCode)
            {
                AppLogger.Debug("Stats", $"Anonymous stats reported: Converted={converted}, Edited={edited}, SavedBytes={savedBytes}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug("Stats", $"Failed to send anonymous stats: {ex.Message}");
        }
    }
}
