https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net/

SpotifyBlazor

A Blazor Web App that integrates with the Spotify Web API to deliver a full music and podcast experience — including playback, liked content, albums, artists, shows, and real‑time telemetry.

Features
```
    Spotify OAuth login + automatic token refresh

    Now Playing with playback controls

    Liked Songs, Saved Albums, Saved Podcasts

    Album, Artist, and Podcast Show pages

    Search across tracks, artists, and albums

    Application Insights telemetry

    Clean Blazor components + modern UI
```
Tech Stack
```
    Blazor Web App (SSR + WASM)

    ASP.NET Core backend

    Spotify Web API

    JWT authentication

    Application Insights

    C# / .NET 8
```
Project Structure
Code
```
SpotifyBlazor/
├── Client/      # Blazor UI
├── Server/      # API, auth, telemetry
└── Shared/      # Models
```
Running Locally
```
    Create a Spotify Developer App

    Add your Client ID + Redirect URI

    Configure JWT + AI settings

    Run backend:
    Code

    dotnet run --project SpotifyBlazor

    Run client:
    Code

    dotnet run --project SpotifyBlazor.Client

    Visit:
    Code

    https://localhost:7151
```
Why This Project Exists

To explore a modern Blazor architecture, real‑world OAuth flows, and a polished media UI — all while integrating a complex external API.
