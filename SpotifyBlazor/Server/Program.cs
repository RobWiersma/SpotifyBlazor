using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using SpotifyBlazor.Client.Services;
using SpotifyBlazor.Shared.Models;
using static System.Net.WebRequestMethods;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// ---------------------------------------------------------
// Apply CORS BEFORE endpoints
// ---------------------------------------------------------
app.UseCors("AllowLocalhost");

app.MapGet("/api/config", (IConfiguration config) =>
{
    return new
    {
        ClientId = config["ConnectionStrings:clientId"],
        CallbackUri = config["ConnectionStrings:callbackUri"]
    };
});

app.MapPost("/api/spotify/exchange", async (
    IHttpClientFactory httpFactory,
    IConfiguration config,
    [FromBody] ExchangeRequest req) =>
{
    var http = httpFactory.CreateClient();

    var values = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = req.Code,
        ["redirect_uri"] = config["ConnectionStrings:callbackUri"],
        ["client_id"] = config["ConnectionStrings:clientId"],
        ["client_secret"] = config["ConnectionStrings:clientSecret"]
    };

    var content = new FormUrlEncodedContent(values);
    var response = await http.PostAsync("https://accounts.spotify.com/api/token", content);

    if (!response.IsSuccessStatusCode)
        return Results.Problem("Spotify token exchange failed.");

    var json = await response.Content.ReadFromJsonAsync<TokenResponseFull>();
    return Results.Ok(json);
});

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
