using Microsoft.AspNetCore.Mvc;
using SpotifyBlazor.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// Application Insights
// ---------------------------------------------------------

builder.Services.AddApplicationInsightsTelemetry();

builder.Logging.AddApplicationInsights();

builder.Logging.AddFilter<
    Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(
    "",
    LogLevel.Information);

// ---------------------------------------------------------
// CORS
// ---------------------------------------------------------

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy
            .WithOrigins(
                "https://localhost:xxxx"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ---------------------------------------------------------
// MVC + Razor + HttpClient
// ---------------------------------------------------------

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

// ---------------------------------------------------------
// Build App
// ---------------------------------------------------------

var app = builder.Build();

// ---------------------------------------------------------
// Pipeline
// ---------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowLocalhost");

// ---------------------------------------------------------
// Endpoints
// ---------------------------------------------------------

app.MapGet("/api/config", (
    IConfiguration config,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Serving /api/config");

    return new
    {
        ClientId = config["ConnectionStrings:clientId"],
        CallbackUri = config["ConnectionStrings:callbackUri"]
    };
});

app.MapPost("/api/spotify/exchange", async (
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<Program> logger,
    [FromBody] ExchangeRequest req) =>
{
    logger.LogInformation(
        "Starting OAuth code exchange {CodeLength}",
        req.Code?.Length);

    var http = httpFactory.CreateClient();

    var values = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = req.Code,
        ["redirect_uri"] = config["ConnectionStrings:callbackUri"],
        ["client_id"] = config["ConnectionStrings:clientId"],
        ["client_secret"] = config["ConnectionStrings:clientSecret"]
    };

    logger.LogInformation("Sending OAuth exchange request to Spotify");

    var start = DateTime.UtcNow;

    var response = await http.PostAsync(
        "https://accounts.spotify.com/api/token",
        new FormUrlEncodedContent(values));

    logger.LogInformation(
        "Spotify responded {StatusCode} after {ElapsedMs}ms",
        response.StatusCode,
        (DateTime.UtcNow - start).TotalMilliseconds);

    if (!response.IsSuccessStatusCode)
    {
        logger.LogWarning(
            "Spotify token exchange failed {StatusCode}",
            response.StatusCode);

        return Results.Problem("Spotify token exchange failed.");
    }

    var json = await response.Content
        .ReadFromJsonAsync<TokenResponseFull>();

    logger.LogInformation(
        "OAuth token received {TokenType} expires_in={ExpiresIn}",
        json?.token_type,
        json?.expires_in);

    return Results.Ok(json);
});

app.MapPost("/api/spotify/refresh", async (
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<Program> logger,
    RefreshRequest req) =>
{
    logger.LogInformation("Starting token refresh");

    var http = httpFactory.CreateClient();

    var values = new Dictionary<string, string>
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = req.RefreshToken,
        ["client_id"] = config["ConnectionStrings:clientId"],
        ["client_secret"] = config["ConnectionStrings:clientSecret"]
    };

    var start = DateTime.UtcNow;

    var response = await http.PostAsync(
        "https://accounts.spotify.com/api/token",
        new FormUrlEncodedContent(values));

    logger.LogInformation(
        "Refresh response {StatusCode} after {ElapsedMs}ms",
        response.StatusCode,
        (DateTime.UtcNow - start).TotalMilliseconds);

    response.EnsureSuccessStatusCode();

    var json = await response.Content
        .ReadFromJsonAsync<TokenResponseFull>();

    logger.LogInformation(
        "Refresh completed {TokenType} expires_in={ExpiresIn}",
        json?.token_type,
        json?.expires_in);

    return Results.Ok(json);
});

app.MapGet("/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("Health check requested");

    return Results.Ok("healthy");
});

app.MapGet("/api/logtest", (ILogger<Program> logger) =>
{
    logger.LogInformation(
        "TEST APPLICATION INSIGHTS LOG {TimeUtc}",
        DateTime.UtcNow);

    return Results.Ok(new { message = "Log test executed" });
});

// ---------------------------------------------------------
// Endpoints
// ---------------------------------------------------------

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
