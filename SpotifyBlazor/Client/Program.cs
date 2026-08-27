using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpotifyBlazor.Client;
using SpotifyBlazor.Client.Services;
using SpotifyBlazor.Shared.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient for calling the Server API
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Client-side services
builder.Services.AddScoped<ConfigService>();
builder.Services.AddScoped<SpotifyAuthService>();
builder.Services.AddScoped<TelemetryService>();

var host = builder.Build();

// Hydrate ConfigService BEFORE rendering UI
var config = host.Services.GetRequiredService<ConfigService>();
await config.LoadAsync();

// Hydrate SpotifyAuthService BEFORE rendering UI
var auth = host.Services.GetRequiredService<SpotifyAuthService>();
await auth.InitializeAsync();

// Run the WASM app
await host.RunAsync();