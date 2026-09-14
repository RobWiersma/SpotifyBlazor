https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net/

SpotifyBlazor — A Modern Spotify Web Client Built with Blazor WebAssembly

A fast, responsive, Spotify‑connected web experience built with Blazor WebAssembly, featuring real‑time playback controls, device switching, dynamic UI layout, and a clean, Spotify‑inspired interface. This project integrates deeply with the Spotify Web API and Web Playback SDK to deliver a native-feeling music experience entirely in the browser.

✨ Features
🎧 Full Spotify Playback Integration
```
    Play, pause, skip, and control volume

    Device switching between available Spotify devices

    Real-time player state updates

    Album art, track metadata, and progress bar
```
📱 Dynamic Player Bar
```
    Auto-resizes based on content

    JS interop for measuring height and updating layout

    CSS variable–driven responsive spacing

    Smooth transitions and mobile-friendly behavior
```
🕒 Recently Played History
```
    Paginated history view

    Artist and album navigation

    Local time formatting

    Clean, Spotify-style track list layout
```
🔐 Secure Authentication
```
    Spotify OAuth 2.0 Authorization Code flow

    Token exchange and refresh

    Local storage persistence

    Logged-in state tracking
```
📊 Telemetry & Diagnostics
```
    Application Insights integration

    Structured logging for API calls, playback events, and UI actions

    Timed operations for performance measurement
```
⚡ Built for Performance
```
    Blazor WebAssembly

    Minimal API backend

    Efficient caching of Spotify responses

    Lightweight JS modules for UI measurement
```
🛠️ Tech Stack
```
Area	Technology
Frontend	Blazor WebAssembly, C#, Razor Components
Backend	.NET Minimal API
Auth	Spotify OAuth 2.0
Playback	Spotify Web Playback SDK
UI	Bootstrap 5, custom CSS
Interop	JavaScript modules (playerbar.js)
Logging	Application Insights
Hosting	Azure App Service
```

🚀 Getting Started
1. Clone the repo
bash

git clone 

2. Configure Spotify API

Create a Spotify app at:
https://developer.spotify.com/dashboard

Add your redirect URI:
Code

https://localhost:7151/auth/callback

Then update your configuration:
Code
```
ClientId: "<your-client-id>"
ClientSecret: "<your-client-secret>"
RedirectUri: "https://localhost:7151/auth/callback"
```
3. Run the project
bash

dotnet run

The app will launch at:
Code

https://localhost:7151

📂 Project Structure
Code
```
SpotifyBlazor/
│
├── Client/                # Blazor WebAssembly frontend
│   ├── Components/        # UI components
│   ├── Services/          # Auth, Playback, Telemetry
│   ├── wwwroot/           # Static assets + JS modules
│   └── App.razor
│
├── Server/                # Minimal API backend
│   └── Controllers/
│
└── Shared/                # Shared models (Track, Artist, History)
```
