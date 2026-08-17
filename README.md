SpotifyBlazor

A lightweight Blazor WebAssembly Spotify client powered by the Spotify Web API and Web Playback SDK.
Browse artists, view albums, play tracks, and explore your Spotify library through a fast, modern UI.
Overview

SpotifyBlazor uses Spotify’s Authorization Code Flow for authentication and integrates directly with the Web Playback SDK for in‑browser audio playback.
The app includes artist pages, album browsing, track playback, search, and a Liked Songs viewer.
Features

    Artist pages with full metadata

    Clickable track playback

    Liked Songs viewer

    Search for artists, albums, and tracks

    Smart “Back” navigation using from= query parameters

    Automatic access token refresh

Token Refresh Logic

Access tokens refresh only when close to expiration:
'''csharp

if (DateTime.UtcNow < ExpiresAt.AddSeconds(-60))
    return false;
'''

This prevents unnecessary refresh calls and keeps playback stable.
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

    Add your client ID and secret to appsettings.json

    Run the server

    Run the Blazor client

    Log in with Spotify

License

MIT
