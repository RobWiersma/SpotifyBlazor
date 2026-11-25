using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using SpotifyBlazor.Client.Services;

namespace SpotifyBlazor.Tests;

// Demo mode answers Spotify's API itself, so the rest of the app works unchanged without an
// account. These go through the same HttpClient handler the app uses.
public class DemoModeTests
{
    private readonly DemoLibrary _demo = new(new NoJsRuntime()) { IsOn = true };
    private readonly RecordingHandler _network = new();
    private readonly HttpClient _http;

    public DemoModeTests()
    {
        _http = new HttpClient(new DemoSpotifyHandler(_demo) { InnerHandler = _network })
        {
            BaseAddress = new Uri("https://localhost/")
        };
    }

    private async Task<JsonNode> GetJsonAsync(string url)
    {
        var response = await _http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact]
    public async Task Answers_as_a_demo_user()
    {
        var me = await GetJsonAsync("https://api.spotify.com/v1/me");

        Assert.Equal("demo", (string?)me["id"]);
        Assert.Equal("Demo listener", (string?)me["display_name"]);
        Assert.Empty(_network.Requests);   // nothing went out to Spotify
    }

    [Fact]
    public async Task Has_two_playlists_whose_tracks_all_exist()
    {
        var playlists = (await GetJsonAsync("https://api.spotify.com/v1/me/playlists"))["items"]!.AsArray();
        Assert.Equal(2, playlists.Count);

        foreach (var playlist in playlists)
        {
            var items = (await GetJsonAsync($"https://api.spotify.com/v1/playlists/{playlist!["id"]}/tracks"))["items"]!.AsArray();
            Assert.NotEmpty(items);

            // Playlist entries use Spotify's newer "item" field (older responses called it "track")
            foreach (var entry in items)
                await GetJsonAsync($"https://api.spotify.com/v1/tracks/{entry!["item"]!["id"]}");   // 200 for each
        }
    }

    [Fact]
    public async Task Albums_list_tracks()
    {
        var albums = (await GetJsonAsync("https://api.spotify.com/v1/me/albums"))["items"]!.AsArray();
        Assert.Equal(2, albums.Count);

        var tracks = (await GetJsonAsync($"https://api.spotify.com/v1/albums/{albums[0]!["album"]!["id"]}/tracks"))["items"]!.AsArray();
        Assert.NotEmpty(tracks);
    }

    [Fact]
    public async Task Search_finds_demo_tracks_by_name()
    {
        var result = await GetJsonAsync("https://api.spotify.com/v1/search?q=funk&type=track");

        var names = result["tracks"]!["items"]!.AsArray().Select(t => (string)t!["name"]!).ToList();
        Assert.Contains(names, n => n.Contains("Funk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_with_no_match_is_empty()
    {
        var result = await GetJsonAsync("https://api.spotify.com/v1/search?q=zzzznothing&type=track");

        Assert.Empty(result["tracks"]!["items"]!.AsArray());
    }

    [Fact]
    public async Task Anything_demo_mode_cant_do_is_a_404_with_an_explanation()
    {
        var response = await _http.GetAsync("https://api.spotify.com/v1/audiobooks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("demo mode", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Telemetry_is_not_sent_in_demo_mode()
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry", new { message = "hi" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_network.Requests);
    }

    [Fact]
    public async Task Our_own_api_still_goes_to_the_server()
    {
        await _http.GetAsync("/api/config");

        Assert.Equal("/api/config", Assert.Single(_network.Requests).AbsolutePath);
    }

    [Fact]
    public async Task With_demo_mode_off_spotify_calls_go_out_as_normal()
    {
        _demo.IsOn = false;

        await _http.GetAsync("https://api.spotify.com/v1/me");

        Assert.Equal("api.spotify.com", Assert.Single(_network.Requests).Host);
    }

    // Records what would have gone out to the network, and answers 200
    private class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    // These tests never play audio, so JavaScript is never needed
    private class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected JS call: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected JS call: {identifier}");
    }
}
