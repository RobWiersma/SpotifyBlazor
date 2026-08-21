https://spotifyblazor-ayb0fch4d9ceaha2.westus3-01.azurewebsites.net/

Spotify Blazor Client

A simple web app that connects to the Spotify API and lets you browse artists, albums, and tracks.
Built with C#, Blazor WebAssembly, and ASP.NET Core.
What This Project Does

    Lets a user log in with their Spotify account

    Shows artists, albums, and track lists

    Displays album covers and artist info

    Lets you click an album to see its tracks

    Uses Spotify’s official API to load real music data

Tech Used

    C# / .NET 8

    Blazor WebAssembly (client‑side UI)

    ASP.NET Core (backend for Spotify login)

    Spotify Web API

Why I Built It

I wanted to practice:

    Working with APIs

    Building UI components in Blazor

    Handling OAuth login flows

    Structuring a clean .NET project

This project helped me sharpen my skills in modern .NET web development.
How It Works (Simple Version)

    You log in with Spotify

    The app gets permission to load your music data

    It shows artists, albums, and tracks

    You can click an album to view its songs

Running the Project

You need:

    .NET 8

    A Spotify Developer account

    A Client ID + Client Secret

Then:
bash

dotnet run --project Server
dotnet run --project Client

Notes

    The app requires Spotify login

    It only works for your own Spotify account

    This project is for learning and showcasing .NET skills
