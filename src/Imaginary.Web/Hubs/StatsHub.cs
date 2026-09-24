using Microsoft.AspNetCore.SignalR;
using Imaginary.Web.Services;

namespace Imaginary.Web.Hubs;

public class StatsHub : Hub
{
    private readonly GlobalStatsService _statsService;

    public StatsHub(GlobalStatsService statsService)
    {
        _statsService = statsService;
    }

    public GlobalStatsDto GetCurrentStats()
    {
        return _statsService.GetStats();
    }
}
