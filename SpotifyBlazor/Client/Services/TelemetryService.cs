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
        _logger.LogInformation(
            "Sending telemetry Level={Level} Message={Message}",
            level,
            message
        );

        var evt = new TelemetryEvent(
            level,
            message,
            context,
            DateTime.UtcNow.ToString("o"),   // clientTime
            "1.0.0",                         // clientVersion
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
                _logger.LogWarning(
                    "Telemetry send failed StatusCode={StatusCode}",
                    resp.StatusCode
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telemetry");
        }
    }

    // ---------------------------------------------------------
    // Convenience: Track a timed operation
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
