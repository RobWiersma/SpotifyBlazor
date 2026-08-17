SpotifyBlazor

A lightweight Blazor WebAssembly client for Spotify.
Browse artists, view albums, play tracks, and explore your library using the Spotify Web API + Web Playback SDK.
Features

    Artist pages with full metadata

    Auto‑generated Top Tracks (popularity‑based)

    Clickable track playback via Spotify Web Playback

    Liked Songs viewer

    Search for artists, albums, and tracks

    Secure OAuth login with automatic token refresh

Tech Stack

    Blazor WebAssembly

    ASP.NET Core backend (OAuth + refresh token endpoint)

    Spotify Web API

    Spotify Web Playback SDK

Token Refresh

Access tokens refresh only when close to expiration:
csharp

if (DateTime.UtcNow < ExpiresAt.AddSeconds(-60))
    return false;

This prevents unnecessary refresh calls and keeps playback stable.
Navigation

Artist pages accept a from parameter:
Code

/artist/{id}?from=liked

This enables a simple “Back” button that returns to the correct page.
Models
SpotifyPaging<T>
csharp

public class SpotifyPaging<T>
{
    public string Href { get; set; } = string.Empty;
    public List<T> Items { get; set; } = new();
    public int Limit { get; set; }
    public string Next { get; set; } = string.Empty;
    public int Offset { get; set; }
    public string Previous { get; set; } = string.Empty;
    public int Total { get; set; }
}

SpotifyTrack
csharp

public class SpotifyTrack
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int DurationMs { get; set; }
    public string Uri { get; set; }
    public SpotifyAlbum Album { get; set; }
    public List<SpotifyArtist> Artists { get; set; }
    public int Popularity { get; set; }
}

Setup

    Create a Spotify Developer App

    Add redirect URI:
    Code

    https://localhost:5001/auth/callback

    Add client ID + secret to appsettings.json

    Run server + client

    Log in with Spotify

    Start browsing and playing music

License

MIT
