using SpotifyBlazor.Shared.Models;

namespace SpotifyBlazor.Client.Services;

// The Spotify sign-in link, shared by the header (desktop) and the player bar's menu (phones)
public static class SpotifyLogin
{
    private const string Scopes =
        "user-read-private " +
        "user-read-email " +
        "user-modify-playback-state " +
        "user-read-playback-state " +
        "user-read-currently-playing " +
        "playlist-read-private " +
        "user-library-read " +
        "user-library-modify " +
        "user-read-recently-played " +
        "streaming";

    // Null until the config (client id and callback) has loaded
    public static string? AuthorizeUrl(ConfigService config)
    {
        if (string.IsNullOrEmpty(config.ClientId) || string.IsNullOrEmpty(config.CallbackUri))
            return null;

        return "https://accounts.spotify.com/authorize" +
            $"?client_id={config.ClientId}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(config.CallbackUri)}" +
            $"&scope={Scopes}";
    }
}
