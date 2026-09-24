using Imaginary.Core;
using Imaginary.Web.Components;
using Imaginary.Web.Hubs;
using Imaginary.Web.Models;
using Imaginary.Web.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Imaginary Core services
builder.Services.AddImaginaryCore();
builder.Services.AddSingleton<TempFileStorageService>();
builder.Services.AddSingleton<GlobalStatsService>();
builder.Services.AddSignalR();

builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = true;
}).AddHubOptions(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024 * 1024; // 64 MB
});

var app = builder.Build();

// Configure SignalR broadcast on stats update
var statsService = app.Services.GetRequiredService<GlobalStatsService>();
var hubContext = app.Services.GetRequiredService<IHubContext<StatsHub>>();
statsService.OnStatsChanged += stats =>
{
    _ = hubContext.Clients.All.SendAsync("StatsUpdated", stats);
};

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SignalR Hub mapping
app.MapHub<StatsHub>("/hubs/stats");

// API: File download endpoint for web-converted images and zip archives
app.MapGet("/api/download/{id}", (string id, TempFileStorageService storage) =>
{
    var item = storage.GetItem(id);
    if (item == null)
    {
        return Results.NotFound();
    }
    return Results.File(item.FilePath, item.ContentType, item.FileName);
});

// API: Global Stats GET
app.MapGet("/api/stats", (GlobalStatsService stats) =>
{
    return Results.Ok(stats.GetStats());
});

// API: Global Stats Increment (used by Desktop app and Web converter)
app.MapPost("/api/stats/increment", (StatsIncrementRequest request, GlobalStatsService stats) =>
{
    if (request == null)
    {
        return Results.BadRequest(new { error = "Invalid request payload" });
    }

    var updated = stats.Increment(
        converted: Math.Max(0, request.Converted),
        edited: Math.Max(0, request.Edited),
        savedBytes: Math.Max(0, request.SavedBytes)
    );

    return Results.Ok(updated);
});

// Convenient download redirects directly to latest GitHub release
const string GitHubLatestExe = "https://github.com/captainiglo31/Imaginary/releases/latest/download/Imaginary.exe";
const string GitHubLatestZip = "https://github.com/captainiglo31/Imaginary/releases/latest/download/Imaginary-Portable-win-x64.zip";

app.MapGet("/download", () => Results.Redirect(GitHubLatestExe, permanent: false));
app.MapGet("/download/exe", () => Results.Redirect(GitHubLatestExe, permanent: false));
app.MapGet("/download/zip", () => Results.Redirect(GitHubLatestZip, permanent: false));
app.MapGet("/download/version", () => Results.Ok(new { version = "2.5.0", platform = "win-x64" }));

app.Run();
