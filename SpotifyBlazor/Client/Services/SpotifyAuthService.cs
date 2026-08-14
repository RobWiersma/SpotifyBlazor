using Microsoft.JSInterop;
using SpotifyBlazor.Shared.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpotifyBlazor.Client.Services;

public class SpotifyAuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public event Action? OnPlaybackChanged;
    private void NotifyPlaybackChanged() => OnPlaybackChanged?.Invoke();

    private const string StorageKey = "spotify_auth";

    public SpotifyAuthService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    // Load tokens from localStorage on startup
    public async Task InitializeAsync()
    {
        var json = await _js.InvokeAsync<string>("localStorage.getItem", StorageKey);

        if (string.IsNullOrEmpty(json))
            return;

        var saved = System.Text.Json.JsonSerializer.Deserialize<SavedAuth>(json);
        if (saved is null)
            return;

        AccessToken = saved.AccessToken;
        RefreshToken = saved.RefreshToken;
        ExpiresAt = saved.ExpiresAt;
    }

    // Persist tokens to localStorage
    private async Task PersistAsync()
    {
        var saved = new SavedAuth
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            ExpiresAt = ExpiresAt
        };

        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    // Called once after initial login (after /api/spotify/exchange)
    public async Task SetInitialTokens(TokenResponseFull token)
    {
        AccessToken = token.access_token;
        RefreshToken = token.refresh_token;
        ExpiresAt = DateTime.UtcNow.AddSeconds(token.expires_in);

        await PersistAsync();

        NotifyStateChanged();
    }

    public async Task<bool> RefreshIfNeededAsync()
    {
        if (string.IsNullOrEmpty(RefreshToken))
            return false;

        if (DateTime.UtcNow < ExpiresAt.AddSeconds(-60))
            return false;

        var response = await _http.PostAsJsonAsync("/api/spotify/refresh",
            new RefreshRequest { RefreshToken = RefreshToken! });

        if (!response.IsSuccessStatusCode)
            return false;

        var token = await response.Content.ReadFromJsonAsync<TokenResponseFull>();
        if (token is null)
            return false;

        AccessToken = token.access_token;

        RefreshToken = token.refresh_token ?? RefreshToken;

        ExpiresAt = DateTime.UtcNow.AddSeconds(token.expires_in);

        await PersistAsync();
        NotifyStateChanged();
        return true;
    }

    // Helper to call Spotify APIs with auto-refresh
    public async Task<HttpResponseMessage> SendSpotifyAsync(HttpRequestMessage req)
    {
        await RefreshIfNeededAsync();

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        return await _http.SendAsync(req);
    }

    // Logout: clear tokens + localStorage
    public async Task LogoutAsync()
    {
        AccessToken = null;
        RefreshToken = null;
        ExpiresAt = DateTime.MinValue;

        await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);

        NotifyStateChanged();
    }

    // Get current user profile
    public async Task<SpotifyUserProfile?> GetUserProfileAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyUserProfile>();
    }

    // Get now playing
    public async Task<SpotifyNowPlaying?> GetNowPlayingAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me/player/currently-playing");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return null;

        if (res.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyNowPlaying>();
    }

    public async Task SkipToNextAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.spotify.com/v1/me/player/next");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        await _http.SendAsync(req);
    }

    public async Task SkipToPreviousAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.spotify.com/v1/me/player/previous");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        await _http.SendAsync(req);
    }

    public async Task PauseAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/pause");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        await _http.SendAsync(req);
    }

    public async Task PlayAsync()
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/play");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        await _http.SendAsync(req);
    }

    public async Task PlayTrackAsync(string trackUri)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/play")
        {
            Content = JsonContent.Create(new
            {
                uris = new[] { trackUri }
            })
        };

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        await _http.SendAsync(req);

        NotifyPlaybackChanged();
    }

    // DTO for localStorage
    private class SavedAuth
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public async Task<SpotifyLikedSongs?> GetLikedSongsAsync(int offset = 0, int limit = 50)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/me/tracks?limit={limit}&offset={offset}");

        if (!string.IsNullOrEmpty(AccessToken))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return null;

        var test = res.Content.ReadAsStringAsync();


        return await res.Content.ReadFromJsonAsync<SpotifyLikedSongs>();

    }

    public async Task<SpotifyAlbum> GetAlbumAsync(string albumId)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/albums/{albumId}");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyAlbum>(json)!;
    }

    public async Task<SpotifyArtistFull?> GetArtistAsync(string artistId)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/artists/{artistId}?market=US");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return null;

        var test = await res.Content.ReadAsStringAsync();

        return await res.Content.ReadFromJsonAsync<SpotifyArtistFull>();
    }

    public async Task<List<SpotifyTrack>> GetArtistTopTracksByPopularityAsync(string artistId)
    {
        await RefreshIfNeededAsync();

        // 1. Get all albums for the artist
        var albums = await GetArtistAlbumsAsync(artistId);
        var tracks = new List<SpotifyTrack>();

        // 2. Fetch tracks for each album
        foreach (var album in albums.Items)
        {
            var albumTracks = await GetAlbumTracksAsync(album.Id);
            if (albumTracks?.Items != null)
                tracks.AddRange(albumTracks.Items);
        }

        // 3. Sort by popularity (descending)
        return tracks
            .OrderBy(t => t.Name)
            .Take(10)
            .ToList();
    }

    public async Task<SpotifyPaging<SpotifyAlbum>> GetArtistAlbumsAsync(string artistId)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/artists/{artistId}/albums?include_groups=album,single,compilation");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return new SpotifyPaging<SpotifyAlbum>();

        return await res.Content.ReadFromJsonAsync<SpotifyPaging<SpotifyAlbum>>();
    }

    public async Task<SpotifyPaging<SpotifyTrack>> GetAlbumTracksAsync(string albumId)
    {
        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/albums/{albumId}/tracks?limit=50");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return new SpotifyPaging<SpotifyTrack>();

        return await res.Content.ReadFromJsonAsync<SpotifyPaging<SpotifyTrack>>();
    }

}
