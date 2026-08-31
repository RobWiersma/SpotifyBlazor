using Microsoft.Extensions.Logging;
using SpotifyBlazor.Shared.Models;
using System.Diagnostics;
using System.Net.Http.Json;

namespace SpotifyBlazor.Client.Services;

public class TelemetryService
{
    private readonly HttpClient _http;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(HttpClient http, ILogger<TelemetryService> logger)
    {
        _http = http;
        _logger = logger;

        _logger.LogWarning("DIAGNOSTIC: TelemetryService  HttpClient hash = {Hash}", _http.GetHashCode());
    }

    // ---------------------------------------------------------
    // Core telemetry send (diagnostic)
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
        // 🔍 DIAGNOSTIC: Log Authorization header BEFORE sending
        var authHeader = _http.DefaultRequestHeaders.Authorization?.ToString();
        //_logger.LogWarning("DIAGNOSTIC: HttpClient Authorization header = {Auth}", authHeader ?? "<null>");

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
