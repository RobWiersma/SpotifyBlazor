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

        _logger.LogWarning("DIAGNOSTIC: TelemetryService HttpClient hash = {Hash}", _http.GetHashCode());
    }

    // ---------------------------------------------------------
    // Guard: Prevent telemetry before login + before JWT exists
    // ---------------------------------------------------------
    private bool CanSendTelemetry()
    {
        if (_auth.State != LoginState.LoggedIn)
        {
            _logger.LogWarning("Telemetry blocked: user not logged in (State={State})", _auth.State);
            return false;
        }

        if (string.IsNullOrEmpty(_auth.ApiJwt))
        {
            _logger.LogWarning("Telemetry blocked: API JWT missing");
            return false;
        }

        return true;
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

        var authHeader = _http.DefaultRequestHeaders.Authorization?.ToString();

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

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Telemetry FAILED StatusCode={StatusCode} AuthHeader={Auth}",
                    resp.StatusCode,
                    authHeader ?? "<null>"
                );
            }
            else
            {
                _logger.LogInformation("Telemetry SUCCESS");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telemetry");
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
        if (!CanSendTelemetry())
            return;

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
