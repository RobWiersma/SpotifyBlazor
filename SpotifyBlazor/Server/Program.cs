using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.ApplicationInsights;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using SpotifyBlazor.Shared.Models;

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

var app = builder.Build();

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

app.MapPost("/api/spotify/exchange", async (
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ExchangeRequest req) =>
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

    var response = await http.PostAsync(
        "https://accounts.spotify.com/api/token",
        new FormUrlEncodedContent(values));

    if (!response.IsSuccessStatusCode)
        return Results.Problem("Spotify token exchange failed.");

    var json = await response.Content.ReadFromJsonAsync<TokenResponseFull>();
    return Results.Ok(json);
});

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
});

// JWT minting
app.MapPost("/api/auth/spotify-login", async (
    IConfiguration config,
    IHttpClientFactory httpFactory,
    SpotifyLoginRequest req) =>
{
    var http = httpFactory.CreateClient();
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", req.SpotifyAccessToken);

    var me = await http.GetFromJsonAsync<SpotifyMe>("https://api.spotify.com/v1/me");

    if (me is null)
        return Results.Unauthorized();

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

    return Results.Ok(new { jwt });
});

// Protected endpoints
app.MapGet("/api/logtest", () => Results.Ok("Log test executed"))
   .RequireAuthorization();

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
.RequireAuthorization();

// Hosted Blazor WASM fallback
app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
