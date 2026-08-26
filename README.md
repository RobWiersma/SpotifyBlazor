https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net/
SpotifyBlazor

SpotifyBlazor is a full‑stack .NET application built with Blazor WebAssembly (client) and ASP.NET Core (API). It integrates with the Spotify Web API using the Authorization Code flow to authenticate users, exchange authorization codes for access/refresh tokens, and retrieve user data such as liked songs, playlists, search results, and profile information.

The project runs entirely in Docker, using a multi‑container architecture with:
```
    Primary + backup API instances

    Automatic failover

    Health checks

    Self‑healing restart policies

    HTTPS‑enabled API containers

    Nginx‑served Blazor WASM client
```
This setup provides a production‑style environment locally, with reliability features typically found in orchestrators like Kubernetes.
Features
```
    Blazor WebAssembly client served via Nginx

    ASP.NET Core API with minimal endpoints

    Spotify OAuth (authorization code flow)

    Token exchange + refresh

    Shared models for tracks, albums, playlists, and user profile

    Docker Compose multi‑container architecture

    Automatic API failover using Docker DNS

    Health checks + restart policies for self‑healing

    HTTPS enabled for API containers using a PFX certificate

    Local development parity with Visual Studio ports
```
Quickstart Setup Guide
1. Clone the repository

2. Create a Spotify Developer App

Go to:
https://developer.spotify.com/dashboard

Set your redirect URI to:
Code
```
http://localhost:7151/callback
```
3. Add your Spotify credentials

Edit appsettings.json in the Server project:
json

```
"ConnectionStrings": {
  "clientId": "YOUR_SPOTIFY_CLIENT_ID",
  "clientSecret": "YOUR_SPOTIFY_CLIENT_SECRET",
  "callbackUri": "http://localhost:7151/callback"
}
```

These values are used by the /api/config, /api/spotify/exchange, and /api/spotify/refresh endpoints.

4. Export an HTTPS certificate for Docker

The API containers run HTTPS internally, so you must export a development certificate:
bash
```
dotnet dev-certs https -ep ./docker-https.pfx -p dockerpassword
```
Place it here:
Code
```
SpotifyBlazor/Server/docker-https.pfx
```
The API Dockerfile automatically loads this into Kestrel.
5. Start the full Docker stack
bash
```
docker compose up --build
```
This launches:
```
    api_primary

    api_backup

    web (Blazor client)
```
Each API container exposes /health for Docker’s self‑healing.
6. Open the client
Code
```
http://localhost:7151
```
Log in with Spotify and the app will begin loading your data.
Technical Overview
Client (Blazor WebAssembly)

The client is a Blazor WASM app built and published via the .NET SDK, then served by Nginx inside a lightweight Alpine container. It communicates with the API using Docker DNS:
```
    http://api_primary:5133

    http://api_backup:5133
```
The client implements automatic failover by attempting requests against the primary API first, and falling back to the backup API if the primary becomes unhealthy.
API (ASP.NET Core)

The API exposes:
```
    GET /api/config – returns Spotify client ID + callback URI

    POST /api/spotify/exchange – exchanges authorization code for tokens

    POST /api/spotify/refresh – refreshes access tokens

    GET /health – used by Docker health checks
```
The API runs both HTTP and HTTPS inside the container:
```
    HTTP: 5133

    HTTPS: 7151
```
Kestrel loads the PFX certificate via environment variables set in the Dockerfile.
Failover & Self‑Healing
Health Checks

Each API container exposes:
Code
```
GET /health → "healthy"
```
Docker uses this to determine container health. If the endpoint fails:

    Docker marks the container as unhealthy

    Docker automatically restarts it

    The Blazor client switches to the backup API

Client‑Side Failover

The client cycles through:

    api_primary

    api_backup

If the primary API is down or unhealthy, the backup instance is used automatically.

This provides zero‑downtime behavior even in local development.
Project Structure
Code

Client/      → Blazor WebAssembly (Nginx)
Server/      → ASP.NET Core API (Kestrel)
Shared/      → Shared models for Spotify data
docker-compose.yml

Running Without Docker

You can also run the project directly from Visual Studio or the .NET CLI:
bash

dotnet run --project SpotifyBlazor/Server
dotnet run --project SpotifyBlazor/Client

However, this disables:
```
    Failover

    Health checks

    Self‑healing

    Multi‑instance API cluster
```
Docker is the recommended environment.

    This project is for learning and showcasing .NET skills
