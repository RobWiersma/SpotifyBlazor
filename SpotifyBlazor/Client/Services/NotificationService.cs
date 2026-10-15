namespace SpotifyBlazor.Client.Services;

public enum ToastLevel
{
    Error,
    Warning
}

public class Toast
{
    public int Id { get; init; }
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; set; } = "";
    public ToastLevel Level { get; init; }
    public int Count { get; set; } = 1;

    // When a rate limit lifts (from Retry-After); the toast shows a countdown to it
    public DateTimeOffset? RetryAt { get; set; }
}

// Holds the toasts shown by the Toasts component. Repeats of the same error
// (e.g. a polling call failing every few seconds) collapse into one toast with a count.
public class NotificationService
{
    private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(8);

    // Rate-limit toasts stay up until the wait is over, but not forever
    private static readonly TimeSpan MaxRetryDisplayTime = TimeSpan.FromMinutes(5);
    private const int MaxToasts = 4;

    private readonly List<Toast> _toasts = new();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private int _nextId;

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? OnChange;

    public void ShowError(string title, string message, string? key = null, DateTimeOffset? retryAt = null) =>
        Show(title, message, ToastLevel.Error, key, retryAt);

    public void ShowWarning(string title, string message, string? key = null) =>
        Show(title, message, ToastLevel.Warning, key, null);

    private void Show(string title, string message, ToastLevel level, string? key, DateTimeOffset? retryAt)
    {
        key ??= $"{title}|{message}";

        var existing = _toasts.FirstOrDefault(t => t.Key == key);
        if (existing != null)
        {
            existing.Count++;
            existing.Message = message;
            existing.RetryAt = retryAt ?? existing.RetryAt;
            StartTimer(existing);
        }
        else
        {
            var toast = new Toast
            {
                Id = ++_nextId,
                Key = key,
                Title = title,
                Message = message,
                Level = level,
                RetryAt = retryAt
            };

            _toasts.Add(toast);

            while (_toasts.Count > MaxToasts)
                Dismiss(_toasts[0].Id);

            StartTimer(toast);
        }

        OnChange?.Invoke();
    }

    public void Dismiss(int id)
    {
        if (_timers.Remove(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_toasts.RemoveAll(t => t.Id == id) > 0)
            OnChange?.Invoke();
    }

    // (Re)starts the auto-dismiss countdown, so a repeating error stays visible.
    // Rate-limit toasts stay until shortly after the wait ends (capped).
    private void StartTimer(Toast toast)
    {
        var id = toast.Id;

        if (_timers.Remove(id, out var old))
        {
            old.Cancel();
            old.Dispose();
        }

        var displayTime = DisplayTime;
        if (toast.RetryAt is { } retryAt)
        {
            var untilRetry = retryAt - DateTimeOffset.Now + TimeSpan.FromSeconds(3);
            displayTime = TimeSpan.FromTicks(Math.Clamp(
                untilRetry.Ticks, DisplayTime.Ticks, MaxRetryDisplayTime.Ticks));
        }

        var cts = new CancellationTokenSource();
        _timers[id] = cts;

        _ = Task.Delay(displayTime, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Dismiss(id);
        }, TaskScheduler.Current);
    }
}
