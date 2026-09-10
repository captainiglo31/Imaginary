using Imaginary.Core;
using Imaginary.Web.Components;
using Imaginary.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Imaginary Core services
builder.Services.AddImaginaryCore();
builder.Services.AddSingleton<TempFileStorageService>();

builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = true;
}).AddHubOptions(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024 * 1024; // 64 MB
});

var app = builder.Build();

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

// File download endpoints
app.MapGet("/api/download/{id}", (string id, TempFileStorageService storage) =>
{
    var item = storage.GetItem(id);
    if (item == null)
    {
        return Results.NotFound();
    }
    return Results.File(item.FilePath, item.ContentType, item.FileName);
});

app.Run();
