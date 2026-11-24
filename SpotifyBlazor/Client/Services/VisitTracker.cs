using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using SpotifyBlazor.Shared.Models;
using System.Net.Http.Json;

namespace SpotifyBlazor.Client.Services;

// Reports each page view to /api/visit for the owner's visitor stats: whether this visitor is
// signed in with Spotify, trying the demo, or signed out. The visitor is a random id kept in
// this browser; signed-in users are recognized on the server from the login token.
public class VisitTracker : IDisposable
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly NavigationManager _nav;
    private readonly SpotifyAuthService _auth;
    private string? _visitorId;
    private bool _started;

    public VisitTracker(HttpClient http, IJSRuntime js, NavigationManager nav, SpotifyAuthService auth)
    {
        _http = http;
        _js = js;
        _nav = nav;
        _auth = auth;
    }

    // Called once JS interop is available (after the first render)
    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _nav.LocationChanged += OnLocationChanged;
        _ = ReportAsync(_nav.Uri);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = ReportAsync(e.Location);

    private async Task ReportAsync(string uri)
    {
        try
        {
            _visitorId ??= await GetVisitorIdAsync();

            var mode = _auth.IsDemo ? VisitModes.Demo
                : _auth.State == LoginState.LoggedIn ? VisitModes.Spotify
                : VisitModes.SignedOut;

            await _http.PostAsJsonAsync("/api/visit", new VisitRequest
            {
                VisitorId = _visitorId,
                Mode = mode,
                Path = new Uri(uri).AbsolutePath,   // no query string: searches stay private
            });
        }
        catch
        {
            // Stats are best-effort; never bother the visitor about them
        }
    }

    private async Task<string> GetVisitorIdAsync()
    {
        try
        {
            var id = await _js.InvokeAsync<string?>("localStorage.getItem", "visitor_id");
            if (!string.IsNullOrEmpty(id))
                return id;

            id = Guid.NewGuid().ToString("N");
            await _js.InvokeVoidAsync("localStorage.setItem", "visitor_id", id);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");  // storage blocked: counts as a new visitor each load
        }
    }

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
