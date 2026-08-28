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
    private readonly ILogger<SpotifyAuthService> _logger;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    public event Action? OnPlaybackChanged;
    private void NotifyPlaybackChanged() => OnPlaybackChanged?.Invoke();

    public string ApiJwt { get; private set; }

    private const string StorageKey = "spotify_auth";

    public SpotifyAuthService(HttpClient http, IJSRuntime js, ILogger<SpotifyAuthService> logger)
    {
        _http = http;
        _js = js;
        _logger = logger;

        _logger.LogWarning("DIAGNOSTIC: AuthService HttpClient hash = {Hash}", _http.GetHashCode());
    }

    private readonly string[] _apiEndpoints = new[]
    {
        "https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net"
    };

    public async Task<T> GetAsync<T>(string path)
    {
        foreach (var api in _apiEndpoints)
        {
            try
            {
                _logger.LogInformation("Calling API {Endpoint}/{Path}", api, path);

                var start = DateTime.UtcNow;
                var result = await _http.GetFromJsonAsync<T>($"{api}/{path}");

                _logger.LogInformation(
                    "API {Endpoint}/{Path} succeeded in {ElapsedMs}ms",
                    api,
                    path,
                    (DateTime.UtcNow - start).TotalMilliseconds
                );

                if (result != null)
                    return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "API {Endpoint}/{Path} failed, trying next endpoint",
                    api,
                    path
                );
            }
        }

        _logger.LogError("All API endpoints failed for path {Path}", path);
        throw new Exception("All API endpoints failed.");
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Loading tokens from localStorage");

        // Load the combined saved auth blob (Spotify tokens)
        var json = await _js.InvokeAsync<string>("localStorage.getItem", StorageKey);

        if (!string.IsNullOrEmpty(json))
        {
            var saved = JsonSerializer.Deserialize<SavedAuthLocal>(json);
            if (saved is not null)
            {
                AccessToken = saved.AccessToken;
                RefreshToken = saved.RefreshToken;
                ExpiresAt = saved.ExpiresAt;

                _logger.LogInformation(
                    "Loaded Spotify tokens. HasAccessToken={HasToken} ExpiresAt={ExpiresAt}",
                    !string.IsNullOrEmpty(AccessToken),
                    ExpiresAt
                );
            }
            else
            {
                _logger.LogWarning("Failed to deserialize saved auth tokens");
            }
        }
        else
        {
            _logger.LogInformation("No saved Spotify tokens found");
        }

        var storedJwt = await _js.InvokeAsync<string>("localStorage.getItem", "api_jwt");

        if (!string.IsNullOrWhiteSpace(storedJwt))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", storedJwt);

            ApiJwt = storedJwt;

            _logger.LogInformation("Restored API JWT into HttpClient");
        }
        else
        {
            _logger.LogInformation("No API JWT found to restore");
        }
    }



    private async Task PersistAsync()
    {
        _logger.LogInformation("Persisting tokens to localStorage");

        var saved = new SavedAuth
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            ExpiresAt = ExpiresAt
        };

        var json = JsonSerializer.Serialize(saved);
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public async Task SetInitialTokens(TokenResponseFull token)
    {
        _logger.LogInformation("Setting initial Spotify tokens");

        // Store Spotify tokens
        AccessToken = token.access_token;
        RefreshToken = token.refresh_token;
        ExpiresAt = DateTime.UtcNow.AddSeconds(token.expires_in);

        _logger.LogInformation(
            "Spotify tokens set. HasAccessToken={HasToken} ExpiresAt={ExpiresAt}",
            !string.IsNullOrEmpty(AccessToken),
            ExpiresAt
        );

        // ---------------------------------------------------------
        // Exchange Spotify token for your API JWT
        // ---------------------------------------------------------
        _logger.LogInformation("Exchanging Spotify token for API JWT");

        var resp = await _http.PostAsJsonAsync("/api/auth/spotify-login",
            new { SpotifyAccessToken = AccessToken });

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "API JWT exchange failed StatusCode={StatusCode}",
                resp.StatusCode
            );
            throw new Exception("Failed to exchange Spotify token for API JWT.");
        }

        var jwtResponse = await resp.Content.ReadFromJsonAsync<ApiJwtResponseLocal>();
        ApiJwt = jwtResponse?.access_token;

        if (string.IsNullOrEmpty(ApiJwt))
        {
            _logger.LogWarning("API JWT was null or empty after exchange");
            throw new Exception("API JWT missing after exchange.");
        }

        _logger.LogInformation("API JWT received and stored");

        // Restore JWT into HttpClient
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiJwt);

        _logger.LogInformation("API JWT applied to HttpClient");

        // ---------------------------------------------------------
        // Persist everything
        // ---------------------------------------------------------
        await PersistAsync();

        _logger.LogInformation("All tokens persisted to localStorage");
    }


    public async Task<bool> RefreshIfNeededAsync()
    {
        // If no refresh token, cannot refresh
        if (string.IsNullOrWhiteSpace(RefreshToken))
            return false;

        // If not expired, nothing to do
        if (DateTime.UtcNow < ExpiresAt.AddSeconds(-30))
            return true;

        try
        {
            // ---------------------------------------------------------
            // STEP 1: Refresh Spotify access token
            // ---------------------------------------------------------
            var refreshResp = await _http.PostAsJsonAsync("/api/spotify/refresh",
                new RefreshRequest { RefreshToken = RefreshToken });

            if (!refreshResp.IsSuccessStatusCode)
                return false;

            var spotify = await refreshResp.Content.ReadFromJsonAsync<TokenResponseFull>();
            if (spotify is null)
                return false;

            AccessToken = spotify.access_token;
            RefreshToken = spotify.refresh_token ?? RefreshToken;
            ExpiresAt = DateTime.UtcNow.AddSeconds(spotify.expires_in);

            await PersistAsync();

            // ---------------------------------------------------------
            // STEP 2: Exchange Spotify access token for new API JWT
            // ---------------------------------------------------------
            var jwtResp = await _http.PostAsJsonAsync("/api/auth/spotify-login",
                new SpotifyLoginRequest { SpotifyAccessToken = AccessToken });

            if (!jwtResp.IsSuccessStatusCode)
                return false;

            var jwtJson = await jwtResp.Content.ReadFromJsonAsync<JsonElement>();
            var apiJwt = jwtJson.GetProperty("jwt").GetString();

            if (string.IsNullOrWhiteSpace(apiJwt))
                return false;

            // ---------------------------------------------------------
            // STEP 3: Apply JWT to HttpClient
            // ---------------------------------------------------------
            ApplyJwt(apiJwt);

            // ---------------------------------------------------------
            // STEP 4: Persist JWT
            // ---------------------------------------------------------
            await _js.InvokeVoidAsync("localStorage.setItem", "api_jwt", apiJwt);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SendClientLogAsync(
        string level,
        string message,
        string? context = null,
        string? page = null,
        string? component = null,
        string? action = null,
        double? durationMs = null)
    {
        _logger.LogInformation(
            "Sending client telemetry Level={Level} Message={Message}",
            level,
            message
        );

        var evt = new TelemetryEvent(
            level,
            message,
            context,
            DateTime.UtcNow.ToString("o"),   // clientTime
            "1.0.0",                         // clientVersion (you can wire this to your build)
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
            else
            {
                _logger.LogInformation("Telemetry sent successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telemetry");
        }
    }



    public async Task<HttpResponseMessage> SendSpotifyAsync(HttpRequestMessage req)
    {
        _logger.LogInformation("SendSpotifyAsync: Preparing request {Method} {Url}",
            req.Method, req.RequestUri);

        await RefreshIfNeededAsync();

        if (!string.IsNullOrEmpty(AccessToken))
        {
            _logger.LogInformation("SendSpotifyAsync: Adding Bearer token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var start = DateTime.UtcNow;
        var res = await _http.SendAsync(req);

        _logger.LogInformation("SendSpotifyAsync: Response {StatusCode} in {ElapsedMs}ms",
            res.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        return res;
    }

    public async Task LogoutAsync()
    {
        _logger.LogInformation("LogoutAsync: Clearing tokens and localStorage");

        AccessToken = null;
        RefreshToken = null;
        ExpiresAt = DateTime.MinValue;

        await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);

        _logger.LogInformation("LogoutAsync: Tokens cleared, notifying state change");
        NotifyStateChanged();
    }

    public async Task<SpotifyUserProfile?> GetUserProfileAsync()
    {
        _logger.LogInformation("GetUserProfileAsync: Fetching user profile");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetUserProfileAsync: Response {StatusCode}", res.StatusCode);

        if (!res.IsSuccessStatusCode)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyUserProfile>();
    }

    public async Task<SpotifyNowPlaying?> GetNowPlayingAsync()
    {
        _logger.LogInformation("GetNowPlayingAsync: Fetching now playing");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me/player/currently-playing");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetNowPlayingAsync: Response {StatusCode}", res.StatusCode);

        if (!res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyNowPlaying>();
    }

    public async Task SkipToNextAsync()
    {
        _logger.LogInformation("SkipToNextAsync: Skipping to next track");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.spotify.com/v1/me/player/next");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("SkipToNextAsync: Response {StatusCode}", res.StatusCode);
    }

    public async Task SkipToPreviousAsync()
    {
        _logger.LogInformation("SkipToPreviousAsync: Skipping to previous track");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.spotify.com/v1/me/player/previous");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("SkipToPreviousAsync: Response {StatusCode}", res.StatusCode);
    }

    public async Task PauseAsync()
    {
        _logger.LogInformation("PauseAsync: Pausing playback");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/pause");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("PauseAsync: Response {StatusCode}", res.StatusCode);
    }

    public async Task PlayAsync()
    {
        _logger.LogInformation("PlayAsync: Resuming playback");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/play");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("PlayAsync: Response {StatusCode}", res.StatusCode);
    }

    public async Task PlayTrackAsync(string trackUri)
    {
        _logger.LogInformation("PlayTrackAsync: Playing track {TrackUri}", trackUri);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.spotify.com/v1/me/player/play")
        {
            Content = JsonContent.Create(new { uris = new[] { trackUri } })
        };

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("PlayTrackAsync: Response {StatusCode}", res.StatusCode);

        NotifyPlaybackChanged();
    }

    public async Task<SpotifyLikedSongs?> GetLikedSongsAsync(int offset = 0, int limit = 50)
    {
        _logger.LogInformation("GetLikedSongsAsync: Fetching liked songs offset={Offset} limit={Limit}",
            offset, limit);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/me/tracks?limit={limit}&offset={offset}");

        if (!string.IsNullOrEmpty(AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetLikedSongsAsync: Response {StatusCode}", res.StatusCode);

        if (!res.IsSuccessStatusCode)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyLikedSongs>();
    }

    public async Task<SpotifyAlbum> GetAlbumAsync(string albumId)
    {
        _logger.LogInformation("GetAlbumAsync: Fetching album {AlbumId}", albumId);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/albums/{albumId}");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetAlbumAsync: Response {StatusCode}", res.StatusCode);

        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyAlbum>(json)!;
    }

    public async Task<SpotifyArtistFull?> GetArtistAsync(string artistId)
    {
        _logger.LogInformation("GetArtistAsync: Fetching artist {ArtistId}", artistId);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/artists/{artistId}?market=US");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetArtistAsync: Response {StatusCode}", res.StatusCode);

        if (!res.IsSuccessStatusCode)
            return null;

        return await res.Content.ReadFromJsonAsync<SpotifyArtistFull>();
    }





    // All updated SpotifyAuthService methods with logging

    public async Task<SpotifyPaging<SpotifyAlbum>> GetArtistAlbumsAsync(
        string artistId,
        int limit = 20,
        int offset = 0)
    {
        _logger.LogInformation("GetArtistAlbumsAsync: artistId={ArtistId}, limit={Limit}, offset={Offset}",
            artistId, limit, offset);

        await RefreshIfNeededAsync();

        var url =
            $"https://api.spotify.com/v1/artists/{artistId}/albums" +
            $"?include_groups=album,single,compilation,appears_on" +
            $"&limit={limit}&offset={offset}";

        _logger.LogInformation("GetArtistAlbumsAsync: GET {Url}", url);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetArtistAlbumsAsync: Response {StatusCode} in {ElapsedMs}ms",
            res.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            _logger.LogWarning("GetArtistAlbumsAsync: Spotify error body={Body}", body);
            return null;
        }

        return await res.Content.ReadFromJsonAsync<SpotifyPaging<SpotifyAlbum>>();
    }

    public async Task<SpotifyPaging<SpotifyTrack>> GetAlbumTracksAsync(string albumId)
    {
        _logger.LogInformation("GetAlbumTracksAsync: albumId={AlbumId}", albumId);

        await RefreshIfNeededAsync();

        var url = $"https://api.spotify.com/v1/albums/{albumId}/tracks?limit=50";
        _logger.LogInformation("GetAlbumTracksAsync: GET {Url}", url);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var res = await _http.SendAsync(req);

        _logger.LogInformation("GetAlbumTracksAsync: Response {StatusCode} in {ElapsedMs}ms",
            res.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        if (!res.IsSuccessStatusCode)
            return new SpotifyPaging<SpotifyTrack>();

        return await res.Content.ReadFromJsonAsync<SpotifyPaging<SpotifyTrack>>();
    }

    public async Task<List<SpotifyAlbum>> GetAllArtistAlbums(string artistId)
    {
        _logger.LogInformation("GetAllArtistAlbums: artistId={ArtistId}", artistId);

        var albums = new List<SpotifyAlbum>();
        int limit = 10;
        int offset = 0;

        while (true)
        {
            _logger.LogInformation("GetAllArtistAlbums: Fetching page offset={Offset}", offset);

            var page = await GetArtistAlbumsAsync(artistId, limit, offset);

            if (page?.Items == null || page.Items.Count == 0)
            {
                _logger.LogInformation("GetAllArtistAlbums: No more items, stopping");
                break;
            }

            albums.AddRange(page.Items);

            offset += limit;

            if (page.Items.Count < limit)
            {
                _logger.LogInformation("GetAllArtistAlbums: Last page reached");
                break;
            }
        }

        _logger.LogInformation("GetAllArtistAlbums: Total albums={Count}", albums.Count);
        return albums;
    }

    public async Task<List<SearchResult>> SearchAsync(string query)
    {
        _logger.LogInformation("SearchAsync: query={Query}", query);

        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track,artist,album&limit=10";
        _logger.LogInformation("SearchAsync: GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(request);

        _logger.LogInformation("SearchAsync: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var results = new List<SearchResult>();

        // Tracks
        if (json.TryGetProperty("tracks", out var tracks) &&
            tracks.TryGetProperty("items", out var trackItems))
        {
            foreach (var item in trackItems.EnumerateArray())
            {
                results.Add(new SearchResult
                {
                    Id = item.GetProperty("uri").GetString(),
                    Type = "track",
                    Name = item.GetProperty("name").GetString(),
                    ImageUrl = item.GetProperty("album")
                                  .GetProperty("images")[0]
                                  .GetProperty("url").GetString()
                });
            }
        }

        // Artists
        if (json.TryGetProperty("artists", out var artists) &&
            artists.TryGetProperty("items", out var artistItems))
        {
            foreach (var item in artistItems.EnumerateArray())
            {
                results.Add(new SearchResult
                {
                    Id = item.GetProperty("id").GetString(),
                    Type = "artist",
                    Name = item.GetProperty("name").GetString(),
                    ImageUrl = item.GetProperty("images").GetArrayLength() > 0
                        ? item.GetProperty("images")[0].GetProperty("url").GetString()
                        : ""
                });
            }
        }

        // Albums
        if (json.TryGetProperty("albums", out var albums) &&
            albums.TryGetProperty("items", out var albumItems))
        {
            foreach (var item in albumItems.EnumerateArray())
            {
                results.Add(new SearchResult
                {
                    Id = item.GetProperty("id").GetString(),
                    Type = "album",
                    Name = item.GetProperty("name").GetString(),
                    ImageUrl = item.GetProperty("images")[0]
                                  .GetProperty("url").GetString()
                });
            }
        }

        _logger.LogInformation("SearchAsync: Total results={Count}", results.Count);
        return results;
    }

    public async Task<SpotifyPlaylistResponse> GetUserPlaylistsAsync()
    {
        _logger.LogInformation("GetUserPlaylistsAsync: Fetching playlists");

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me/playlists");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetUserPlaylistsAsync: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyPlaylistResponse>(raw);
    }

    public async Task<SpotifyPlaylist> GetPlaylistAsync(string playlistId)
    {
        _logger.LogInformation("GetPlaylistAsync: playlistId={PlaylistId}", playlistId);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/playlists/{playlistId}");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetPlaylistAsync: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyPlaylist>(raw);
    }

    public async Task<List<PlaylistTrackItem>> GetPlaylistTracksAsync(string playlistId)
    {
        _logger.LogInformation("GetPlaylistTracksAsync: playlistId={PlaylistId}", playlistId);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/playlists/{playlistId}/items");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetPlaylistTracksAsync: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PlaylistTrackResponse>(raw);

        return result.Items;
    }

    public async Task<List<SpotifyAlbum>> GetArtistAlbumsFirstPage(string artistId)
    {
        _logger.LogInformation("GetArtistAlbumsFirstPage: artistId={ArtistId}", artistId);

        await RefreshIfNeededAsync();

        var limit = 10;
        var offset = 0;

        var url =
            $"https://api.spotify.com/v1/artists/{artistId}/albums" +
            $"?include_groups=album,single,compilation,appears_on" +
            $"&limit={limit}&offset={offset}";

        _logger.LogInformation("GetArtistAlbumsFirstPage: GET {Url}", url);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetArtistAlbumsFirstPage: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<SpotifyPaging<SpotifyAlbum>>(raw);

        return page?.Items ?? new List<SpotifyAlbum>();
    }

    public async Task<SpotifyPaging<SpotifyAlbum>> GetArtistAlbumsPage(
        string artistId, int offset, int limit)
    {
        _logger.LogInformation("GetArtistAlbumsPage: artistId={ArtistId}, offset={Offset}, limit={Limit}",
            artistId, offset, limit);

        await RefreshIfNeededAsync();

        var url =
            $"https://api.spotify.com/v1/artists/{artistId}/albums" +
            $"?include_groups=album,single,compilation,appears_on" +
            $"&limit={limit}&offset={offset}";

        _logger.LogInformation("GetArtistAlbumsPage: GET {Url}", url);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetArtistAlbumsPage: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyPaging<SpotifyAlbum>>(raw);
    }

    public async Task<SpotifyPaging<SpotifyAlbum>> GetArtistAlbumsByUrl(string url)
    {
        _logger.LogInformation("GetArtistAlbumsByUrl: GET {Url}", url);

        await RefreshIfNeededAsync();

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        var start = DateTime.UtcNow;
        var response = await _http.SendAsync(req);

        _logger.LogInformation("GetArtistAlbumsByUrl: Response {StatusCode} in {ElapsedMs}ms",
            response.StatusCode,
            (DateTime.UtcNow - start).TotalMilliseconds);

        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyPaging<SpotifyAlbum>>(raw);
    }

    public void ApplyJwt(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return;

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt);

        ApiJwt = jwt;
    }

    public async Task<bool> CompleteLoginAsync(string spotifyAccessToken)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/spotify-login",
            new SpotifyLoginRequest { SpotifyAccessToken = spotifyAccessToken });

        if (!resp.IsSuccessStatusCode)
            return false;

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = json.GetProperty("jwt").GetString();

        if (string.IsNullOrWhiteSpace(jwt))
            return false;

        ApplyJwt(jwt);

        await _js.InvokeVoidAsync("localStorage.setItem", "api_jwt", jwt);
        return true;
    }

}
