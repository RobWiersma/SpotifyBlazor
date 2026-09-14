using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog.Core;
using SpotifyBlazor.Shared.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Application Insights
builder.Services.AddApplicationInsightsTelemetry();
builder.Logging.AddApplicationInsights();
builder.Logging.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(
    "", LogLevel.Information);

// MVC + Razor + Controllers
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// HttpClient factory
builder.Services.AddHttpClient();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ----------------------------------------------------
// RATE LIMITING (burst + steady via stacked policies)
// ----------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    // Global steady limit: 100 req/min per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Burst limiter for token endpoints
    options.AddPolicy<string>("TokenBurst", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // Steady limiter for token endpoints
    options.AddPolicy<string>("TokenSteady", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // Burst limiter for telemetry
    options.AddPolicy<string>("TelemetryBurst", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 5,
                QueueLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // Steady limiter for telemetry
    options.AddPolicy<string>("TelemetrySteady", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

var app = builder.Build();

var startupLogger = app.Services
    .GetRequiredService<ILogger<Program>>();

startupLogger.LogWarning(
    "ENV CHECK → Issuer={Issuer}, Audience={Audience}, KeyLength={KeyLength}, ASPNETCORE_ENVIRONMENT={Env}",
    jwtIssuer,
    jwtAudience,
    jwtKey?.Length,
    app.Environment.EnvironmentName
);

// Hosted Blazor WASM pipeline
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();   // ⭐ Serve Client WASM
app.UseStaticFiles();            // ⭐ Serve Client static assets

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ⭐ Enable rate limiting middleware
app.UseRateLimiter();

// -----------------------------
// PUBLIC ENDPOINTS
// -----------------------------

app.MapGet("/api/config", (IConfiguration config) =>
{
    return new
    {
        ClientId = config["ConnectionStrings:clientId"],
        CallbackUri = config["ConnectionStrings:callbackUri"]
    };
});

// TOKEN EXCHANGE (burst + steady)
app.MapPost("/api/spotify/exchange", async (
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ExchangeRequest req,
    ILogger<Program> logger) =>
{
    logger.LogWarning("EXCHANGE → Starting Spotify code exchange");

    var http = httpFactory.CreateClient();

    var form = new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = req.Code,
        ["redirect_uri"] = config["ConnectionStrings:callbackUri"],
        ["client_id"] = config["ConnectionStrings:clientId"],
        ["client_secret"] = config["ConnectionStrings:clientSecret"]
    };

    var response = await http.PostAsync(
        "https://accounts.spotify.com/api/token",
        new FormUrlEncodedContent(form));

    var raw = await response.Content.ReadAsStringAsync();
    logger.LogWarning("EXCHANGE RAW RESPONSE → {Raw}", raw);

    if (!response.IsSuccessStatusCode)
        return Results.Problem("Spotify token exchange failed");

    var token = JsonSerializer.Deserialize<TokenResponseFull>(raw);

    return Results.Ok(token);
})
.RequireRateLimiting("TokenBurst")
.RequireRateLimiting("TokenSteady");

// REFRESH TOKEN (burst + steady)
app.MapPost("/api/spotify/refresh", async (
    IHttpClientFactory httpFactory,
    IConfiguration config,
    RefreshRequest req) =>
{
    var http = httpFactory.CreateClient();

    var values = new Dictionary<string, string>
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = req.RefreshToken,
        ["client_id"] = config["ConnectionStrings:clientId"],
        ["client_secret"] = config["ConnectionStrings:clientSecret"]
    };

    var response = await http.PostAsync(
        "https://accounts.spotify.com/api/token",
        new FormUrlEncodedContent(values));

    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadFromJsonAsync<TokenResponseFull>();
    return Results.Ok(json);
})
.RequireRateLimiting("TokenBurst")
.RequireRateLimiting("TokenSteady");

// JWT MINT (burst + steady)
app.MapPost("/api/auth/spotify-login", async (
    IConfiguration config,
    IHttpClientFactory httpFactory,
    SpotifyLoginRequest req,
    ILogger<Program> logger) =>
{
    logger.LogWarning("JWT MINT → Starting mint with access token length {Len}",
        req.SpotifyAccessToken?.Length);

    var http = httpFactory.CreateClient();
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", req.SpotifyAccessToken);

    SpotifyMe? me = null;

    try
    {
        me = await http.GetFromJsonAsync<SpotifyMe>("https://api.spotify.com/v1/me");
        logger.LogWarning("JWT MINT → Spotify /me result: {Me}", me);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "JWT MINT → Spotify /me call failed");
        return Results.Problem("Spotify /me failed");
    }

    if (me is null)
    {
        logger.LogWarning("JWT MINT → Spotify /me returned null");
        return Results.Unauthorized();
    }

    // Mint JWT
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, me.Id),
        new Claim("spotify_name", me.DisplayName ?? ""),
        new Claim("spotify_uri", me.Uri ?? "")
    };

    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"],
        audience: config["Jwt:Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    logger.LogWarning("JWT MINT → Successfully minted JWT length {Len}", jwt.Length);

    return Results.Ok(new { jwt });
})
.RequireRateLimiting("TokenBurst")
.RequireRateLimiting("TokenSteady");

// Protected endpoints
app.MapGet("/api/logtest", () => Results.Ok("Log test executed"))
   .RequireAuthorization();

// TELEMETRY (burst + steady)
app.MapPost("/api/telemetry", async (
    TelemetryEvent evt,
    ClaimsPrincipal user,
    TelemetryClient telemetry) =>
{
    var userId =
        user.FindFirstValue(ClaimTypes.NameIdentifier) ??
        user.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
        "unknown";

    var props = new Dictionary<string, string>
    {
        ["userId"] = userId,
        ["level"] = evt.Level,
        ["context"] = evt.Context ?? "",
        ["clientTime"] = evt.ClientTime ?? "",
        ["clientVersion"] = evt.ClientVersion ?? "",
        ["clientPage"] = evt.Page ?? "",
        ["clientComponent"] = evt.Component ?? "",
        ["clientAction"] = evt.Action ?? ""
    };

    var metrics = new Dictionary<string, double>();
    if (evt.DurationMs.HasValue)
        metrics["durationMs"] = evt.DurationMs.Value;

    telemetry.TrackEvent(evt.Message, props, metrics);

    return Results.Accepted();
})
.RequireAuthorization()
.RequireRateLimiting("TelemetryBurst")
.RequireRateLimiting("TelemetrySteady");

app.MapGet("/env", (IConfiguration config) =>
{
    return new
    {
        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        JwtAudience = config["Jwt:Audience"],
        JwtIssuer = config["Jwt:Issuer"],
        JwtKeyLen = config["Jwt:Key"]?.Length,
        ConnStringsClientId = config["ConnectionStrings:clientId"],
        ConnStringsCallbackUri = config["ConnectionStrings:callbackUri"]
    };
});

// Hosted Blazor WASM fallback
app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
