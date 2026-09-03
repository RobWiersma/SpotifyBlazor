using System.Diagnostics;
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
        [JsonPropertyName("height")]
        public int? Height { get; set; }
        [JsonPropertyName("width")]
        public int? Width { get; set; }
        [JsonPropertyName("url")]
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
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }

        [JsonPropertyName("track_number")]
        public int TrackNumber { get; set; }

        [JsonPropertyName("disc_number")]
        public int DiscNumber { get; set; }

        [JsonPropertyName("explicit")]
        public bool Explicit { get; set; }

        [JsonPropertyName("is_local")]
        public bool IsLocal { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("external_urls")]
        public Dictionary<string, string> ExternalUrls { get; set; }

        [JsonPropertyName("artists")]
        public List<SpotifyArtist> Artists { get; set; }

        [JsonPropertyName("album")]
        public SpotifyAlbum? Album { get; set; }
    }


    public class SpotifyArtist
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("external_urls")]
        public Dictionary<string, string> ExternalUrls { get; set; }
    }

    public class SpotifyAlbum
    {
        [JsonPropertyName("album_type")]
        public string AlbumType { get; set; }

        [JsonPropertyName("total_tracks")]
        public int TotalTracks { get; set; }

        [JsonPropertyName("external_urls")]
        public Dictionary<string, string> ExternalUrls { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage> Images { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; }

        [JsonPropertyName("release_date_precision")]
        public string ReleaseDatePrecision { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("artists")]
        public List<SpotifyArtist> Artists { get; set; }

        [JsonPropertyName("tracks")]
        public SpotifyAlbumTracks Tracks { get; set; }

        [JsonPropertyName("copyrights")]
        public List<SpotifyCopyright> Copyrights { get; set; }

        [JsonPropertyName("external_ids")]
        public SpotifyExternalIds ExternalIds { get; set; }

        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }
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

    public class SpotifyAlbumTracks
    {
        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("previous")]
        public string Previous { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("items")]
        public List<SpotifyTrack> Items { get; set; }

    }

    public class SpotifyCopyright
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }



    public class SpotifyExternalIds
    {
        [JsonPropertyName("upc")]
        public string Upc { get; set; }
    }

    public class SpotifyArtistFull
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("external_urls")]
        public Dictionary<string, string> ExternalUrls { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage> Images { get; set; }

        // Optional fields (Spotify includes them in full artist objects)
        [JsonPropertyName("followers")]
        public SpotifyFollowers Followers { get; set; }

        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }

        [JsonPropertyName("popularity")]
        public int Popularity { get; set; }
    }

    public class SpotifyFollowers
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class SpotifyTopTracks
    {
        [JsonPropertyName("tracks")]
        public List<SpotifyTrack> Tracks { get; set; }
    }

    public class SpotifyPaging<T>
    {
        [JsonPropertyName("href")]
        public string Href { get; set; } = string.Empty;

        [JsonPropertyName("items")]
        public List<T> Items { get; set; } = new();

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("previous")]
        public string Previous { get; set; } = string.Empty;

        [JsonPropertyName("total")]
        public int Total { get; set; }

    }

    public class SearchResult
    {
        public string Id { get; set; }
        public string Type { get; set; } // track, artist, album
        public string Name { get; set; }
        public string ImageUrl { get; set; }
    }

    public class SpotifyPlaylistResponse
    {
        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("previous")]
        public string Previous { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("items")]
        public List<SpotifyPlaylist> Items { get; set; }
    }

    public class SpotifyPlaylist
    {
        [JsonPropertyName("collaborative")]
        public bool Collaborative { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("external_urls")]
        public ExternalUrls ExternalUrls { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage> Images { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("owner")]
        public SpotifyUser Owner { get; set; }

        [JsonPropertyName("primary_color")]
        public string PrimaryColor { get; set; }

        [JsonPropertyName("public")]
        public bool Public { get; set; }

        [JsonPropertyName("snapshot_id")]
        public string SnapshotId { get; set; }

        // IMPORTANT: this is NOT the track list
        [JsonPropertyName("items")]
        public PlaylistItems Items { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }
    }


    public class ExternalUrls
    {
        [JsonPropertyName("spotify")]
        public string Spotify { get; set; }
    }

    public class SpotifyUser
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }

        [JsonPropertyName("external_urls")]
        public ExternalUrls ExternalUrls { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }
    }

    public class PlaylistItems
    {
        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class PlaylistTracks
    {
        public int Total { get; set; }
    }

    public class GenreSeedResponse
    {
        public List<string> Genres { get; set; }
    }

    public class PlaylistTrackItem
    {
        [JsonPropertyName("added_at")]
        public DateTime AddedAt { get; set; }

        [JsonPropertyName("added_by")]
        public SpotifyUser AddedBy { get; set; }

        [JsonPropertyName("is_local")]
        public bool IsLocal { get; set; }

        [JsonPropertyName("primary_color")]
        public string PrimaryColor { get; set; }

        // IMPORTANT: this is "item", not "track"
        [JsonPropertyName("item")]
        public PlaylistTrack Track { get; set; }

        [JsonPropertyName("video_thumbnail")]
        public VideoThumbnail VideoThumbnail { get; set; }
    }

    public class PlaylistTrackResponse
    {
        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("items")]
        public List<PlaylistTrackItem> Items { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("previous")]
        public string Previous { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class Track
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("artists")]
        public List<SimpleArtist> Artists { get; set; }

        [JsonPropertyName("album")]
        public SimpleAlbum Album { get; set; }
    }

    public class SimpleArtist
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class SimpleAlbum
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage> Images { get; set; }
    }

    public class PlaylistTrack
    {
        [JsonPropertyName("is_playable")]
        public bool IsPlayable { get; set; }

        [JsonPropertyName("explicit")]
        public bool Explicit { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("episode")]
        public bool Episode { get; set; }

        [JsonPropertyName("track")]
        public bool TrackFlag { get; set; }

        [JsonPropertyName("album")]
        public SimpleAlbum Album { get; set; }

        [JsonPropertyName("artists")]
        public List<SimpleArtist> Artists { get; set; }

        [JsonPropertyName("disc_number")]
        public int DiscNumber { get; set; }

        [JsonPropertyName("track_number")]
        public int TrackNumber { get; set; }

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }

        [JsonPropertyName("external_ids")]
        public ExternalIds ExternalIds { get; set; }

        [JsonPropertyName("external_urls")]
        public ExternalUrls ExternalUrls { get; set; }

        [JsonPropertyName("href")]
        public string Href { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("is_local")]
        public bool IsLocal { get; set; }
    }

    public class ExternalIds
    {
        [JsonPropertyName("isrc")]
        public string Isrc { get; set; }
    }

    public class VideoThumbnail
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class SavedAuth
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class SpotifyLoginRequest
    {
        public string SpotifyAccessToken { get; set; } = "";
    }

    public class SpotifyMe
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Uri { get; set; }
    }

    public class SavedAuthLocal
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string ApiJwt { get; set; }
    }

    public class ApiJwtResponseLocal
    {
        public string jwt { get; set; }
    }

    public enum LoginState
    {
        NotLoggedIn,
        LoggingIn,
        LoggedIn,
        LoginFailed
    }

    public record TelemetryEvent(string Level, string Message, string? Context, string? ClientTime, string? ClientVersion, string? Page, string? Component, string? Action, double? DurationMs);
}
