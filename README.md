https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net/

# SpotifyBlazor

![.NET](https://img.shields.io/badge/.NET-7.0-blueviolet)
![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4)
![C#](https://img.shields.io/badge/C%23-Developer-blue)
![Spotify Web API](https://img.shields.io/badge/Spotify-Web%20API-1DB954)
![Spotify Web Playback SDK](https://img.shields.io/badge/Spotify-Web%20Playback%20SDK-1DB954)
![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Active-success)
![Platform](https://img.shields.io/badge/Platform-Web-lightgrey)
![Build](https://img.shields.io/badge/Build-Passing-brightgreen)
![App Insights](https://img.shields.io/badge/Application%20Insights-Enabled-purple)
![Auth](https://img.shields.io/badge/Auth-JWT%20%2B%20OAuth-orange)

A modern Blazor WebAssembly app that integrates with the Spotify Web API to deliver a full music + podcast experience—playback (including right in the browser), liked content, albums, artists, shows, search, and real‑time telemetry.

---

## Overview

SpotifyBlazor is a full‑stack .NET application that brings Spotify’s ecosystem into a clean, responsive Blazor interface. It supports OAuth login, playback control on any Spotify Connect device, in-browser playback via the Spotify Web Playback SDK, browsing liked songs/albums/shows, album & artist pages, podcast show pages, and Application Insights telemetry.

---

## Features

- Spotify OAuth login + automatic token refresh  
- Now Playing with playback controls, seek, volume, and queue  
- **Play in the browser** — the app registers itself as a Spotify Connect device ("This browser")  
- Device picker to move playback between your phone, desktop, speakers, and the browser  
- Liked Songs, Saved Albums, Saved Podcasts  
- Album, Artist, and Podcast Show pages  
- Search across tracks, artists, albums  
- Application Insights telemetry  
- Clean Blazor components + modern UI  

---

## Tech Stack

- Blazor WebAssembly, hosted by an ASP.NET Core server  
- Spotify Web API + Spotify Web Playback SDK  
- JWT authentication  
- Rate limiting on token and telemetry endpoints  
- Application Insights  
- C# / .NET 7  

---

## Project Structure

```
SpotifyBlazor/
├── Client/      # Blazor WASM UI
│   ├── Pages/           # Routed pages (Liked, Album, Artist, Search, ...)
│   ├── Shared/          # PlayerBar, DevicePicker, VolumePopup, ...
│   ├── Services/        # SpotifyAuthService, WebPlayerService, TelemetryService
│   └── wwwroot/         # Static assets + JS interop (webplayer.js, playerbar.js, ...)
├── Server/      # API: token exchange/refresh, JWT, telemetry
└── Shared/      # Models
```

---

## Authentication

1. The user logs in with Spotify (authorization code flow). Spotify redirects back to `/spotcallback`.
2. The client sends the code to `POST /api/spotify/exchange`; the server swaps it for access + refresh tokens using the client secret, which never leaves the server.
3. The client exchanges the Spotify access token for an app JWT via `POST /api/auth/spotify-login`, used for protected endpoints like telemetry.
4. Tokens are kept in `localStorage`. Shortly before the access token expires, the client calls `POST /api/spotify/refresh` and mints a fresh JWT. Only one refresh runs at a time — concurrent API calls wait for it and reuse the new token, which keeps them under the token endpoint rate limits.

Tokens and authorization codes are never written to logs or telemetry.

---

## In-Browser Playback (Web Playback SDK)

The app loads the [Spotify Web Playback SDK](https://developer.spotify.com/documentation/web-playback-sdk) after login, turning the browser tab into a Spotify Connect device named **SpotifyBlazor Web Player** (shown as **🌐 This browser** in the Devices menu).

**Requirements**

- Spotify **Premium** (free accounts get an "account_error"; the Devices menu shows a message)
- The `streaming` scope — already requested at login. If you logged in before it was added, log out and back in.
- HTTPS (or localhost) and a browser with DRM (EME) support — Chrome, Edge, and Firefox work

**How it works**

- `wwwroot/webplayer.js` loads the SDK on demand and creates the player. Whenever the SDK needs a token it calls back into .NET (`WebPlayerService.GetAccessToken`), which refreshes first if needed.
- `WebPlayerService` starts the player on login, stops it on logout, and tracks the device ID and errors. Play/pause/track changes in the tab refresh the player bar.
- If you press play and Spotify reports **no active device**, the play call is retried on the browser player — so with nothing else playing, music just starts in the browser. If another device is active, playback goes there; switch to the browser from **🎧 Devices**.
- Browsers block audio until a user gesture, so audio is unlocked on your first click anywhere in the app.

---

## Configuration

Server settings (`Server/appsettings.json`, overridden per environment):

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:clientId` | Spotify app client ID |
| `ConnectionStrings:clientSecret` | Spotify app client secret (server only) |
| `ConnectionStrings:callbackUri` | Redirect URI — must exactly match one registered in the Spotify dashboard |
| `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` | App JWT signing and validation |
| `ApplicationInsights:ConnectionString` | Telemetry destination |

Keep secrets out of source control: use `dotnet user-secrets` locally and App Service application settings in Azure.

---

## Running Locally

1. Create an app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard).
2. Add a redirect URI, e.g. `https://127.0.0.1:7151/spotcallback` (Spotify no longer accepts `localhost` redirect URIs; use the loopback IP).
3. Put the client ID, secret, and redirect URI in `Server/appsettings.Development.json` (or user-secrets), along with the JWT and Application Insights settings.
4. Run the server project, which also serves the WASM client:

   ```bash
   dotnet run --project SpotifyBlazor/Server
   ```

5. Visit https://127.0.0.1:7151 and log in with Spotify.

---

## Troubleshooting

- **"No music playing" when pressing play** — no Spotify device is active and the web player isn't ready. Open **🎧 Devices** and check for **This browser** or an error message.
- **Web player error in the Devices menu** — "needs Spotify Premium" means a free account; an authentication error usually means your login predates the `streaming` scope — log out and back in.
- **Plays but no sound** — click anywhere on the page once; browsers block audio until you interact.
- **Everything stops after about an hour** — token refresh is failing; check the server logs for `/api/spotify/refresh` errors (e.g. rate limiting or a changed client secret).
