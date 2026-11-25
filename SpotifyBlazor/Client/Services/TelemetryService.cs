using Microsoft.Extensions.Logging;
using SpotifyBlazor.Shared.Models;
using System.Diagnostics;
using System.Net.Http.Json;

namespace SpotifyBlazor.Client.Services;

public class TelemetryService
{
    private readonly HttpClient _http;
    private readonly SpotifyAuthService _auth;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(HttpClient http, SpotifyAuthService auth, ILogger<TelemetryService> logger)
    {
        _http = http;
        _auth = auth;
        _logger = logger;
    }

    // ---------------------------------------------------------
    // Guard: telemetry needs our API's login token, so it's skipped when signed out or in
    // demo mode. That's normal, so it's only a debug message.
    // ---------------------------------------------------------
    private bool CanSendTelemetry()
    {
        if (_auth.State == LoginState.LoggedIn && !string.IsNullOrEmpty(_auth.ApiJwt))
            return true;

        _logger.LogDebug("Telemetry skipped: not signed in (State={State})", _auth.State);
        return false;
    }

    // ---------------------------------------------------------
    // Core telemetry send
    // ---------------------------------------------------------
    public async Task TrackAsync(
        string level,
        string message,
        string? context = null,
        string? page = null,
        string? component = null,
        string? action = null,
        double? durationMs = null)
    {
        if (!CanSendTelemetry())
            return;

        var evt = new TelemetryEvent(
            level,
            message,
            context,
            DateTime.UtcNow.ToString("o"),
            "1.0.0",
            page,
            component,
            action,
            durationMs
        );

        try
        {
            var resp = await _http.PostAsJsonAsync("/api/telemetry", evt);

            // Never log the Authorization header: it's the visitor's login token
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telemetry failed: {StatusCode}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't send telemetry");
        }
    }

    // ---------------------------------------------------------
    // Timed telemetry
    // ---------------------------------------------------------
    public async Task TrackTimedAsync(
        string level,
        string message,
        Func<Task> action,
        string? context = null,
        string? page = null,
        string? component = null,
        string? actionName = null)
    {
        // The work runs either way; only the timing report needs telemetry (demo mode has none)
        if (!CanSendTelemetry())
        {
            await action();
            return;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            await action();
        }
        finally
        {
            sw.Stop();
            await TrackAsync(
                level,
                message,
                context,
                page,
                component,
                actionName,
                sw.Elapsed.TotalMilliseconds
            );
        }
    }
}
