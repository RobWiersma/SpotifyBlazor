using Microsoft.JSInterop;
using SpotifyBlazor.Shared.Models;

namespace SpotifyBlazor.Client.Services;

// Hosts the Spotify Web Playback SDK so this browser tab shows up as a
// Spotify Connect device. Requires the "streaming" scope and a Premium account.
public class WebPlayerService : IAsyncDisposable
{
    public const string PlayerName = "SpotifyBlazor Web Player";

    private readonly IJSRuntime _js;
    private readonly SpotifyAuthService _auth;
    private readonly ILogger<WebPlayerService> _logger;

    private IJSObjectReference? _module;
    private DotNetObjectReference<WebPlayerService>? _dotnetRef;
    private bool _starting;

    public string? DeviceId { get; private set; }
    public bool IsReady { get; private set; }
    public string? Error { get; private set; }

    public event Action? OnChange;

    public WebPlayerService(IJSRuntime js, SpotifyAuthService auth, ILogger<WebPlayerService> logger)
    {
        _js = js;
        _auth = auth;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (_starting || _module != null || _auth.State != LoginState.LoggedIn)
            return;

        _starting = true;

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", "./webplayer.js");
            _dotnetRef = DotNetObjectReference.Create(this);

            var connected = await _module.InvokeAsync<bool>("connect", _dotnetRef, PlayerName, 0.5);
            if (!connected)
                SetError("Couldn't connect the web player.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web player failed to start");
            SetError("Couldn't load the web player.");
        }
        finally
        {
            _starting = false;
        }
    }

    // Must be called from a click handler, before any await, so the browser
    // treats it as a user gesture and allows audio.
    public ValueTask ActivateAsync() =>
        _module?.InvokeVoidAsync("activate") ?? ValueTask.CompletedTask;

    public async Task StopAsync()
    {
        if (_module != null)
        {
            try { await _module.InvokeVoidAsync("disconnect"); } catch { }
        }

        IsReady = false;
        DeviceId = null;
        _auth.FallbackDeviceId = null;
        OnChange?.Invoke();
    }

    // ---------------------------------------------------------
    // Callbacks from webplayer.js
    // ---------------------------------------------------------

    [JSInvokable]
    public async Task<string?> GetAccessToken()
    {
        await _auth.RefreshIfNeededAsync();
        return _auth.AccessToken;
    }

    [JSInvokable]
    public void OnReady(string deviceId)
    {
        _logger.LogInformation("Web player ready");
        DeviceId = deviceId;
        IsReady = true;
        Error = null;
        _auth.FallbackDeviceId = deviceId;
        OnChange?.Invoke();
    }

    [JSInvokable]
    public void OnNotReady(string deviceId)
    {
        _logger.LogInformation("Web player went offline");
        IsReady = false;
        _auth.FallbackDeviceId = null;
        OnChange?.Invoke();
    }

    [JSInvokable]
    public async Task OnStateChanged(bool hasState)
    {
        // Playback on this tab changed; let the player bar refresh
        await _auth.NotifyPlaybackChangedAsync();
    }

    [JSInvokable]
    public void OnError(string type, string message)
    {
        _logger.LogWarning("Web player {Type}: {Message}", type, message);

        SetError(type == "account_error"
            ? "Playing in the browser needs Spotify Premium."
            : $"Web player error: {message}");
    }

    private void SetError(string message)
    {
        Error = message;
        IsReady = false;
        _auth.FallbackDeviceId = null;
        OnChange?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        if (_module != null)
        {
            try { await _module.DisposeAsync(); } catch { }
        }

        _dotnetRef?.Dispose();
    }
}
