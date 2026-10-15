using System.Net;
using System.Text.Json;

namespace SpotifyBlazor.Client.Services;

// Sits on the app's HttpClient and raises a toast for every failed call to
// Spotify or our own API, using the error message from the response body.
public class ErrorNotificationHandler : DelegatingHandler
{
    private readonly NotificationService _notifications;

    public ErrorNotificationHandler(NotificationService notifications)
    {
        _notifications = notifications;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        // Telemetry failures are expected before login and would just be noise
        if (path.StartsWith("/api/telemetry", StringComparison.OrdinalIgnoreCase))
            return await base.SendAsync(request, cancellationToken);

        var source = request.RequestUri?.Host.Contains("spotify.com") == true ? "Spotify" : "Server";
        var endpoint = $"{request.Method} {path.Replace("/v1/", "/")}";

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _notifications.ShowError($"{source} unreachable", $"{endpoint}: {ex.Message}", key: $"net|{endpoint}");
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Buffer so the caller can still read the body after we do
            await response.Content.LoadIntoBufferAsync();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var status = (int)response.StatusCode;
            var message = ExtractMessage(body) ?? response.ReasonPhrase ?? "Request failed";

            var title = $"{source} error {status}";
            var text = $"{endpoint}: {message}";
            var key = $"{status}|{endpoint}";

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAt = GetRetryAt(response);

                // Spotify's CORS rules hide Retry-After from browsers on direct calls,
                // so only responses relayed through our server can carry a wait time
                if (retryAt == null)
                {
                    text += source == "Spotify"
                        ? " (Spotify didn't share a wait time; usually it's a few seconds to a few minutes.)"
                        : " (Try again in a moment.)";
                }

                _notifications.ShowError($"{source} rate limit", text, key, retryAt);
            }
            else if (status >= 500)
            {
                _notifications.ShowError(title, text, key);
            }
            else
            {
                _notifications.ShowWarning(title, text, key);
            }
        }

        return response;
    }

    // Retry-After is either a number of seconds or an HTTP date
    private static DateTimeOffset? GetRetryAt(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
            return DateTimeOffset.Now + delta;

        if (retryAfter?.Date is { } date)
            return date;

        return null;
    }

    // Spotify: {"error":{"status":429,"message":"..."}} or {"error":"...","error_description":"..."}
    // Our server: problem+json {"title":"...","detail":"..."}
    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var msg))
                    return msg.GetString();

                if (root.TryGetProperty("error_description", out var desc))
                    return desc.GetString();

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();
            }

            if (root.TryGetProperty("detail", out var detail))
                return detail.GetString();

            if (root.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch (JsonException)
        {
        }

        // Plain text body; keep toasts short
        return body.Length > 200 ? body[..200] + "…" : body;
    }
}
