using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SpotifyBlazor.Shared.Models
{
    public class ExchangeRequest
    {
        public string Code { get; set; }
    }

    public class TokenResponseFull
    {
        [JsonPropertyName("access_token")]
        public string access_token { get; set; }

        [JsonPropertyName("refresh_token")]
        public string refresh_token { get; set; }

        [JsonPropertyName("expires_in")]
        public int expires_in { get; set; }

        [JsonPropertyName("token_type")]
        public string token_type { get; set; }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string RefreshToken { get; set; }

    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; }
    }

    public class SpotifyUserProfile
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage> Images { get; set; }
    }

    public class SpotifyImage
    {
        public int Height { get; set; }
        public int Width { get; set; }
        public string? Url { get; set; }
    }

    public class SpotifyNowPlaying
    {
        [JsonPropertyName("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonPropertyName("progress_ms")]
        public int ProgressMs { get; set; }

        [JsonPropertyName("item")]
        public SpotifyTrack Item { get; set; }

    }

    public class SpotifyTrack
    {
        public string? Name { get; set; }
        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }
        public bool IsPlayable { get; set; }
        public bool IsLocal { get; set; }
        public int Popularity { get; set; }
        public string? PreviewUrl { get; set; }
        public int TrackNumber { get; set; }
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }
        public string? Id { get; set; }
        public string? Href { get; set; }

        public List<SpotifyArtist> Artists { get; set; } = new();
        public SpotifyAlbum Album { get; set; } = default!;
    }

    public class SpotifyArtist
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
        public Dictionary<string, string>? ExternalUrls { get; set; }
    }

    public class SpotifyAlbum
    {
        public string? AlbumType { get; set; }
        public string? Name { get; set; }
        public List<string> AvailableMarkets { get; set; } = new();
        public List<SpotifyImage> Images { get; set; } = new();
        public List<SpotifyArtist> Artists { get; set; } = new();
    }

    public class ConfigService
    {
        private readonly HttpClient _http;

        public string? ClientId { get; private set; }
        public string? CallbackUri { get; private set; }
        public bool IsLoaded { get; private set; }

        public event Action? OnChange;

        public ConfigService(HttpClient http)
        {
            _http = http;
        }

        public async Task LoadAsync()
        {
            try
            {
                var cfg = await _http.GetFromJsonAsync<ConfigModel>("/api/config");

                if (cfg is not null)
                {
                    ClientId = cfg.ClientId;
                    CallbackUri = cfg.CallbackUri;
                    IsLoaded = true;
                }
                else
                {
                    IsLoaded = false;
                }
            }
            catch
            {
                IsLoaded = false;
            }

            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }

    public class ConfigModel
    {
        public string? ClientId { get; set; }
        public string? CallbackUri { get; set; }
    }

    public class SpotifyLikedSongs
    {
        public string? Href { get; set; }
        public int Limit { get; set; }
        public string? Next { get; set; }
        public int Offset { get; set; }
        public string? Previous { get; set; }
        public int Total { get; set; }

        public List<LikedSongItem> Items { get; set; } = new();
    }

    public class LikedSongItem
    {
        public DateTime AddedAt { get; set; }

        // IMPORTANT: JSON uses "track"
        public SpotifyTrack Track { get; set; } = default!;
    }



}
