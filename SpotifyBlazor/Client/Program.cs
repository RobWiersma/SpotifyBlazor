using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using SpotifyBlazor.Client;
using SpotifyBlazor.Client.Services;
using SpotifyBlazor.Shared.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The browser console shows everything from Information up while developing, and only
// warnings and errors on the live site
builder.Logging.SetMinimumLevel(builder.HostEnvironment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning);

builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<DemoLibrary>();

builder.Services.AddScoped<HttpClient>(sp =>
{
    // Failed responses (Spotify or our API) pop up as toasts. In demo mode, Spotify calls
    // are answered by the demo library instead of going out.
    var handler = new ErrorNotificationHandler(sp.GetRequiredService<NotificationService>())
    {
        InnerHandler = new DemoSpotifyHandler(sp.GetRequiredService<DemoLibrary>())
        {
            InnerHandler = new HttpClientHandler()
        }
    };

    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
    return client;
});

// Client-side services
builder.Services.AddScoped<ConfigService>();
builder.Services.AddScoped<SpotifyAuthService>();
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<WebPlayerService>();
builder.Services.AddScoped<VisitTracker>();

var host = builder.Build();

// Hydrate ConfigService BEFORE rendering UI
var config = host.Services.GetRequiredService<ConfigService>();
await config.LoadAsync();

// Hydrate SpotifyAuthService BEFORE rendering UI
var auth = host.Services.GetRequiredService<SpotifyAuthService>();
await auth.InitializeFromStorageAsync();
await auth.InitializeAsync();


// Run the WASM app
await host.RunAsync();
