# SpotifyBlazor

![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)
![Blazor](https://img.shields.io/badge/Blazor-WebApp-512BD4)
![C#](https://img.shields.io/badge/C%23-Developer-blue)
![Spotify Web API](https://img.shields.io/badge/Spotify-Web%20API-1DB954)
![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Active-success)
![Platform](https://img.shields.io/badge/Platform-Web-lightgrey)
![Build](https://img.shields.io/badge/Build-Passing-brightgreen)
![App Insights](https://img.shields.io/badge/Application%20Insights-Enabled-purple)
![Auth](https://img.shields.io/badge/Auth-JWT%20%2B%20OAuth-orange)

A modern Blazor Web App that integrates with the Spotify Web API to deliver a full music + podcast experience—playback, liked content, albums, artists, shows, search, and real‑time telemetry.

---

## Overview

SpotifyBlazor is a full‑stack .NET application that brings Spotify’s ecosystem into a clean, responsive Blazor interface. It supports OAuth login, playback control, browsing liked songs/albums/shows, album & artist pages, podcast show pages, and Application Insights telemetry.

---

## Features

- Spotify OAuth login + automatic token refresh  
- Now Playing with playback controls  
- Liked Songs, Saved Albums, Saved Podcasts  
- Album, Artist, and Podcast Show pages  
- Search across tracks, artists, albums  
- Application Insights telemetry  
- Clean Blazor components + modern UI  

---

## Tech Stack

- Blazor Web App (SSR + WASM)  
- ASP.NET Core backend  
- Spotify Web API  
- JWT authentication  
- Application Insights  
- C# / .NET 8  

---

## Project Structure

```
SpotifyBlazor/
├── Client/      # Blazor UI
├── Server/      # API, auth, telemetry
└── Shared/      # Models
```


## Running Locally

Create a Spotify Developer App

Add your Client ID + Redirect URI

Configure JWT + AI settings

Run backend: dotnet run --project SpotifyBlazor

Visit: https://localhost:7151
