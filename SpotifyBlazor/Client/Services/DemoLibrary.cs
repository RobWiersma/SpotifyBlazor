using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;

namespace SpotifyBlazor.Client.Services;

// Demo mode, for visitors without a Spotify account (or who'd rather not sign in).
// DemoSpotifyHandler sends the app's Spotify Web API calls here instead of to Spotify, and this
// answers them from a small built-in library, in the same JSON shapes Spotify uses, so every
// page works unchanged. The songs are free Creative Commons tracks by Kevin MacLeod that play
// in the page (demoaudio.js), so playback is real and the visualizer draws the actual audio.
public class DemoLibrary
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _audio;
    private DotNetObjectReference<DemoLibrary>? _selfRef;

    public DemoLibrary(IJSRuntime js) => _js = js;

    public bool IsOn { get; set; }

    // Raised when playback changes on its own (a song ending), so the player bar refreshes
    public event Func<Task>? Changed;

    public const string ProfileId = "demo";
    public const string DeviceId = "demo-device";

    // ---------------------------------------------------------
    // The library
    // ---------------------------------------------------------

    private record Track(string Id, string Name, string AlbumId, int Number, int DurationMs, string File);
    private record Album(string Id, string Name, string Cover, string Year, string[] TrackIds);
    private record Playlist(string Id, string Name, string Description, string Cover, string[] TrackIds);

    private const string ArtistId = "demoKevinMacLeod";
    private const string ArtistName = "Kevin MacLeod";
    private const string ArtistImage = "demo/covers/kevin-macleod.svg";

    // Songs are trimmed to about 90 seconds to keep the download small
    private static readonly Track[] Tracks =
    {
        new("demoFunkorama",        "Funkorama",                 "demoGrooveSampler",   1, 90_000, "funkorama.mp3"),
        new("demoDiscoMedusae",     "Disco Medusae",             "demoGrooveSampler",   2, 90_000, "disco-medusae.mp3"),
        new("demoClubDiver",        "Club Diver",                "demoGrooveSampler",   3, 90_000, "club-diver.mp3"),
        new("demoLocalForecast",    "Local Forecast - Elevator", "demoGrooveSampler",   4, 90_000, "local-forecast-elevator.mp3"),
        new("demoVolatileReaction", "Volatile Reaction",         "demoElectricSampler", 1, 90_000, "volatile-reaction.mp3"),
        new("demoOuroboros",        "Ouroboros",                 "demoElectricSampler", 2, 90_000, "ouroboros.mp3"),
        new("demoVoxelRevolution",  "Voxel Revolution",          "demoElectricSampler", 3, 90_000, "voxel-revolution.mp3"),
        new("demoGalacticRap",      "Galactic Rap",              "demoElectricSampler", 4, 90_000, "galactic-rap.mp3"),
    };

    private static readonly Album[] Albums =
    {
        new("demoGrooveSampler", "Groove Sampler", "demo/covers/groove-sampler.svg", "2026",
            new[] { "demoFunkorama", "demoDiscoMedusae", "demoClubDiver", "demoLocalForecast" }),
        new("demoElectricSampler", "Electric Sampler", "demo/covers/electric-sampler.svg", "2026",
            new[] { "demoVolatileReaction", "demoOuroboros", "demoVoxelRevolution", "demoGalacticRap" }),
    };

    private static readonly Playlist[] Playlists =
    {
        new("demoVisualizerWorkout", "Visualizer Workout",
            "Big beats to show off the visualizer. Try 🌈 Visualizer on Now Playing.",
            "demo/covers/visualizer-workout.svg",
            new[] { "demoVolatileReaction", "demoFunkorama", "demoClubDiver", "demoVoxelRevolution", "demoGalacticRap" }),
        new("demoLateNightLounge", "Late Night Lounge",
            "Something smoother.",
            "demo/covers/late-night-lounge.svg",
            new[] { "demoLocalForecast", "demoDiscoMedusae", "demoOuroboros" }),
    };

    private static Track TrackById(string id) => Tracks.First(t => t.Id == id);
    private static Album AlbumOf(Track t) => Albums.First(a => a.Id == t.AlbumId);

    // What the visitor likes and saves this visit; starts with everything
    private readonly List<string> _liked = Tracks.Select(t => t.Id).ToList();
    private readonly HashSet<string> _savedAlbums = Albums.Select(a => a.Id).ToHashSet();

    // ---------------------------------------------------------
    // Player
    // ---------------------------------------------------------

    private List<string> _context = new();      // track ids of what's playing (album, playlist, …)
    private string? _contextUri;
    private int _index = -1;                    // position in _context
    private readonly List<string> _queue = new(); // added with "Add to queue", played before the context continues
    private string? _currentId;
    private bool _playing;
    private bool _shuffle;
    private string _repeat = "off";             // off, context, track
    private int _volume = 70;
    private readonly Random _random = new();

    // Recently played; a few earlier plays so the page isn't empty on a first visit
    private readonly List<(string Id, DateTime At)> _history = new()
    {
        ("demoClubDiver", DateTime.UtcNow.AddHours(-3)),
        ("demoOuroboros", DateTime.UtcNow.AddHours(-3.1)),
        ("demoLocalForecast", DateTime.UtcNow.AddDays(-1)),
        ("demoFunkorama", DateTime.UtcNow.AddDays(-1.05)),
        ("demoGalacticRap", DateTime.UtcNow.AddDays(-2)),
    };

    private async Task<IJSObjectReference> AudioAsync()
    {
        if (_audio != null)
            return _audio;

        _audio = await _js.InvokeAsync<IJSObjectReference>("import", "./demoaudio.js");
        _selfRef = DotNetObjectReference.Create(this);
        await _audio.InvokeVoidAsync("init", _selfRef);
        return _audio;
    }

    private async Task StartCurrentAsync(int positionMs = 0)
    {
        if (_currentId == null)
            return;

        var track = TrackById(_currentId);
        var audio = await AudioAsync();
        _playing = await audio.InvokeAsync<bool>("play", $"demo/audio/{track.File}", positionMs, _volume);
        _history.Insert(0, (track.Id, DateTime.UtcNow));
    }

    private async Task<int> PositionAsync()
    {
        if (_audio == null || _currentId == null)
            return 0;

        var state = await _audio.InvokeAsync<JsonElement>("state");
        _playing = state.GetProperty("playing").GetBoolean();
        return state.GetProperty("positionMs").GetInt32();
    }

    // Moves to the next song: the queue first, then the context. Returns false at the end.
    private bool Advance()
    {
        if (_queue.Count > 0)
        {
            _currentId = _queue[0];
            _queue.RemoveAt(0);
            return true;
        }

        if (_context.Count == 0)
            return false;

        if (_shuffle && _context.Count > 1)
        {
            int next;
            do next = _random.Next(_context.Count); while (next == _index);
            _index = next;
        }
        else if (_index + 1 < _context.Count)
        {
            _index++;
        }
        else if (_repeat == "context")
        {
            _index = 0;
        }
        else
        {
            return false;
        }

        _currentId = _context[_index];
        return true;
    }

    [JSInvokable]
    public async Task OnSongEnded()
    {
        if (_repeat == "track" || Advance())
            await StartCurrentAsync();
        else
            _playing = false;

        if (Changed != null)
            await Changed.Invoke();
    }

    public async Task StopAsync()
    {
        if (_audio != null)
            await _audio.InvokeVoidAsync("stop");

        _playing = false;
        _currentId = null;
    }

    private List<string> ContextTracks(string uri)
    {
        var parts = uri.Split(':');
        var id = parts.Last();

        if (uri.StartsWith("spotify:album:"))
            return Albums.FirstOrDefault(a => a.Id == id)?.TrackIds.ToList() ?? new();

        if (uri.StartsWith("spotify:playlist:"))
            return Playlists.FirstOrDefault(p => p.Id == id)?.TrackIds.ToList() ?? new();

        if (uri.StartsWith("spotify:artist:"))
            return Tracks.Select(t => t.Id).ToList();

        if (uri.EndsWith(":collection") || uri == "spotify:collection:tracks")
            return _liked.ToList();

        return new();
    }

    // ---------------------------------------------------------
    // Requests
    // ---------------------------------------------------------

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath.StartsWith("/v1/") ? uri.AbsolutePath[4..] : uri.AbsolutePath.TrimStart('/');
        var query = ParseQuery(uri.Query);
        var method = request.Method;
        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        int Int(string key, int fallback) =>
            query.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

        switch (seg)
        {
            case ["me"]:
                return Json(new JsonObject
                {
                    ["id"] = ProfileId,
                    ["display_name"] = "Demo listener",
                    ["type"] = "user",
                    ["uri"] = $"spotify:user:{ProfileId}",
                    ["country"] = "US",
                    ["product"] = "premium",
                    ["images"] = new JsonArray(),
                    ["followers"] = new JsonObject { ["total"] = 0 },
                });

            // --- Player ---

            case ["me", "player"] when method == HttpMethod.Get:
                return Json(await PlayerStateAsync());

            case ["me", "player"] when method == HttpMethod.Put:   // transfer playback: there's only this device
                return NoContent();

            case ["me", "player", "devices"]:
                return Json(new JsonObject { ["devices"] = new JsonArray(Device()) });

            case ["me", "player", "play"]:
                await PlayAsync(request);
                return NoContent();

            case ["me", "player", "pause"]:
                if (_audio != null) await _audio.InvokeVoidAsync("pause");
                _playing = false;
                return NoContent();

            case ["me", "player", "next"]:
                if (Advance()) await StartCurrentAsync();
                return NoContent();

            case ["me", "player", "previous"]:
                if (await PositionAsync() > 3000 || _index <= 0 || _context.Count == 0)
                {
                    if (_audio != null) await _audio.InvokeVoidAsync("seek", 0);
                }
                else
                {
                    _index--;
                    _currentId = _context[_index];
                    await StartCurrentAsync();
                }
                return NoContent();

            case ["me", "player", "seek"]:
                if (_audio != null) await _audio.InvokeVoidAsync("seek", Int("position_ms", 0));
                return NoContent();

            case ["me", "player", "volume"]:
                _volume = Math.Clamp(Int("volume_percent", _volume), 0, 100);
                if (_audio != null) await _audio.InvokeVoidAsync("setVolume", _volume);
                return NoContent();

            case ["me", "player", "shuffle"]:
                _shuffle = query.GetValueOrDefault("state") == "true";
                return NoContent();

            case ["me", "player", "repeat"]:
                _repeat = query.GetValueOrDefault("state") ?? "off";
                return NoContent();

            case ["me", "player", "queue"] when method == HttpMethod.Post:
                var queued = query.GetValueOrDefault("uri")?.Split(':').Last();
                if (queued != null && Tracks.Any(t => t.Id == queued))
                    _queue.Add(queued);
                return NoContent();

            case ["me", "player", "queue"]:
                return Json(new JsonObject
                {
                    ["currently_playing"] = _currentId == null ? null : FullTrack(TrackById(_currentId)),
                    ["queue"] = new JsonArray(UpNext().Select(id => (JsonNode)FullTrack(TrackById(id))).ToArray()),
                });

            case ["me", "player", "recently-played"]:
                return Json(new JsonObject
                {
                    ["items"] = new JsonArray(_history.Take(Int("limit", 20)).Select(h => (JsonNode)new JsonObject
                    {
                        ["track"] = FullTrack(TrackById(h.Id)),
                        ["played_at"] = h.At.ToString("o"),
                        ["context"] = null,
                    }).ToArray()),
                    ["limit"] = Int("limit", 20),
                    ["next"] = null,
                    ["href"] = "",
                });

            // --- Library ---

            case ["me", "tracks"]:
                return Json(Paging(_liked.Select((id, i) => (JsonNode)new JsonObject
                {
                    ["added_at"] = DateTime.UtcNow.AddDays(-i - 1).ToString("o"),
                    ["track"] = FullTrack(TrackById(id)),
                }).ToList(), Int("offset", 0), Int("limit", 20)));

            case ["me", "albums"]:
                return Json(Paging(Albums.Where(a => _savedAlbums.Contains(a.Id)).Select(a => (JsonNode)new JsonObject
                {
                    ["added_at"] = DateTime.UtcNow.AddDays(-7).ToString("o"),
                    ["album"] = FullAlbum(a),
                }).ToList(), Int("offset", 0), Int("limit", 20)));

            case ["me", "shows"]:
                return Json(Paging(new List<JsonNode>(), 0, Int("limit", 20)));

            case ["me", "playlists"]:
                return Json(Paging(Playlists.Select(p => (JsonNode)SimplePlaylist(p)).ToList(), Int("offset", 0), Int("limit", 50)));

            case ["me", "library", "contains"]:
                return Json(new JsonArray(Uris(query).Select(u => (JsonNode)JsonValue.Create(IsSaved(u))).ToArray()));

            case ["me", "library"] when method == HttpMethod.Put:
                foreach (var u in Uris(query)) Save(u, true);
                return NoContent();

            case ["me", "library"] when method == HttpMethod.Delete:
                foreach (var u in Uris(query)) Save(u, false);
                return NoContent();

            // --- Catalog ---

            case ["albums", var id] when Albums.Any(a => a.Id == id):
                return Json(FullAlbum(Albums.First(a => a.Id == id)));

            case ["albums", var id, "tracks"] when Albums.Any(a => a.Id == id):
                return Json(Paging(Albums.First(a => a.Id == id).TrackIds.Select(t => (JsonNode)SimpleTrack(TrackById(t))).ToList(),
                    Int("offset", 0), Int("limit", 50)));

            case ["artists", ArtistId]:
                return Json(FullArtist());

            case ["artists", ArtistId, "albums"]:
                return Json(Paging(Albums.Select(a => (JsonNode)SimpleAlbum(a)).ToList(), Int("offset", 0), Int("limit", 20)));

            case ["artists", ArtistId, "top-tracks"]:
                return Json(new JsonObject { ["tracks"] = new JsonArray(Tracks.Select(t => (JsonNode)FullTrack(t)).ToArray()) });

            case ["tracks", var id] when Tracks.Any(t => t.Id == id):
                return Json(FullTrack(TrackById(id)));

            case ["playlists", var id] when Playlists.Any(p => p.Id == id):
                return Json(FullPlaylist(Playlists.First(p => p.Id == id)));

            case ["playlists", var id, "items" or "tracks"] when Playlists.Any(p => p.Id == id):
                return Json(PlaylistItems(Playlists.First(p => p.Id == id), Int("offset", 0), Int("limit", 100)));

            case ["search"]:
                return Json(Search(query.GetValueOrDefault("q") ?? "", query.GetValueOrDefault("type") ?? "track",
                    Int("offset", 0), Int("limit", 10)));
        }

        return Error(HttpStatusCode.NotFound, "That isn't part of demo mode. Sign in with Spotify to use it.");
    }

    private async Task PlayAsync(HttpRequestMessage request)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            // Resume
            if (_currentId == null)
                return;

            var audio = await AudioAsync();
            _playing = await audio.InvokeAsync<bool>("play", $"demo/audio/{TrackById(_currentId).File}", null, _volume);
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var position = root.TryGetProperty("position_ms", out var pos) && pos.ValueKind == JsonValueKind.Number ? pos.GetInt32() : 0;

        if (root.TryGetProperty("uris", out var uris))
        {
            _contextUri = null;
            _context = uris.EnumerateArray().Select(u => u.GetString()!.Split(':').Last())
                .Where(id => Tracks.Any(t => t.Id == id)).ToList();
            _index = 0;
        }
        else if (root.TryGetProperty("context_uri", out var ctx))
        {
            _contextUri = ctx.GetString();
            _context = ContextTracks(_contextUri ?? "");
            _index = 0;

            if (root.TryGetProperty("offset", out var offset))
            {
                if (offset.TryGetProperty("position", out var p))
                    _index = p.GetInt32();
                else if (offset.TryGetProperty("uri", out var u))
                    _index = Math.Max(0, _context.IndexOf(u.GetString()!.Split(':').Last()));
            }
        }

        if (_context.Count == 0)
            return;

        _index = Math.Clamp(_index, 0, _context.Count - 1);
        _currentId = _context[_index];
        await StartCurrentAsync(position);
    }

    private IEnumerable<string> UpNext()
    {
        var after = _index >= 0 && _index + 1 < _context.Count ? _context.Skip(_index + 1) : Enumerable.Empty<string>();
        return _queue.Concat(after).Take(20);
    }

    private async Task<JsonObject> PlayerStateAsync()
    {
        var position = await PositionAsync();
        var loaded = _currentId != null;

        return new JsonObject
        {
            ["device"] = Device(),
            ["shuffle_state"] = _shuffle,
            ["smart_shuffle"] = false,
            ["repeat_state"] = _repeat,
            ["is_playing"] = _playing,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["context"] = _contextUri == null ? null : new JsonObject
            {
                ["type"] = _contextUri.Split(':')[1],
                ["uri"] = _contextUri,
                ["href"] = "",
                ["external_urls"] = new JsonObject(),
            },
            ["progress_ms"] = position,
            ["item"] = loaded ? FullTrack(TrackById(_currentId!)) : null,
            ["currently_playing_type"] = loaded ? "track" : "unknown",
            ["actions"] = new JsonObject
            {
                ["disallows"] = new JsonObject { ["resuming"] = !loaded || _playing, ["pausing"] = !_playing },
            },
        };
    }

    private JsonObject Device() => new()
    {
        ["id"] = DeviceId,
        ["is_active"] = true,
        ["is_private_session"] = false,
        ["is_restricted"] = false,
        ["name"] = "This browser (demo)",
        ["type"] = "Computer",
        ["volume_percent"] = _volume,
        ["supports_volume"] = true,
    };

    // ---------------------------------------------------------
    // Library edits
    // ---------------------------------------------------------

    private static IEnumerable<string> Uris(Dictionary<string, string> query) =>
        (query.GetValueOrDefault("uris") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

    private bool IsSaved(string uri)
    {
        var id = uri.Split(':').Last();
        return uri.StartsWith("spotify:album:") ? _savedAlbums.Contains(id) : _liked.Contains(id);
    }

    private void Save(string uri, bool save)
    {
        var id = uri.Split(':').Last();

        if (uri.StartsWith("spotify:album:"))
        {
            if (save) _savedAlbums.Add(id); else _savedAlbums.Remove(id);
        }
        else if (Tracks.Any(t => t.Id == id))
        {
            _liked.Remove(id);
            if (save) _liked.Insert(0, id);   // newest first, like Spotify
        }
    }

    // ---------------------------------------------------------
    // Search: matches song, album and artist names
    // ---------------------------------------------------------

    private static JsonObject Search(string q, string types, int offset, int limit)
    {
        var words = q.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        bool Matches(params string[] fields) =>
            words.Length > 0 && words.All(w => fields.Any(f => f.ToLowerInvariant().Contains(w)));

        var result = new JsonObject();
        var wanted = types.Split(',');

        if (wanted.Contains("track"))
            result["tracks"] = Paging(Tracks.Where(t => Matches(t.Name, ArtistName, AlbumOf(t).Name))
                .Select(t => (JsonNode)FullTrack(t)).ToList(), offset, limit);

        if (wanted.Contains("album"))
            result["albums"] = Paging(Albums.Where(a => Matches(a.Name, ArtistName))
                .Select(a => (JsonNode)SimpleAlbum(a)).ToList(), offset, limit);

        if (wanted.Contains("artist"))
            result["artists"] = Paging(Matches(ArtistName) ? new List<JsonNode> { FullArtist() } : new List<JsonNode>(), offset, limit);

        if (wanted.Contains("playlist"))
            result["playlists"] = Paging(new List<JsonNode>(), offset, limit);

        return result;
    }

    // ---------------------------------------------------------
    // JSON in Spotify's shapes
    // ---------------------------------------------------------

    private static JsonArray Images(string url) =>
        new(new JsonObject { ["url"] = url, ["height"] = 640, ["width"] = 640 });

    private static JsonObject ExternalUrls() => new() { ["spotify"] = "https://incompetech.com" };

    private static JsonObject SimpleArtist() => new()
    {
        ["id"] = ArtistId,
        ["name"] = ArtistName,
        ["type"] = "artist",
        ["uri"] = $"spotify:artist:{ArtistId}",
        ["href"] = "",
        ["external_urls"] = ExternalUrls(),
    };

    private static JsonObject FullArtist()
    {
        var artist = SimpleArtist();
        artist["images"] = Images(ArtistImage);
        artist["genres"] = new JsonArray("funk", "electronic", "lounge");
        artist["followers"] = new JsonObject { ["total"] = 0, ["href"] = null };
        artist["popularity"] = 50;
        return artist;
    }

    private static JsonObject SimpleAlbum(Album a) => new()
    {
        ["id"] = a.Id,
        ["name"] = a.Name,
        ["type"] = "album",
        ["album_type"] = "album",
        ["uri"] = $"spotify:album:{a.Id}",
        ["href"] = "",
        ["images"] = Images(a.Cover),
        ["artists"] = new JsonArray(SimpleArtist()),
        ["release_date"] = a.Year,
        ["release_date_precision"] = "year",
        ["total_tracks"] = a.TrackIds.Length,
        ["external_urls"] = ExternalUrls(),
    };

    private static JsonObject FullAlbum(Album a)
    {
        var album = SimpleAlbum(a);
        album["tracks"] = Paging(a.TrackIds.Select(id => (JsonNode)SimpleTrack(TrackById(id))).ToList(), 0, 50);
        album["label"] = "incompetech.com";
        album["popularity"] = 50;
        album["genres"] = new JsonArray();
        album["copyrights"] = new JsonArray(new JsonObject
        {
            ["text"] = "Music by Kevin MacLeod (incompetech.com), licensed under CC BY 4.0",
            ["type"] = "C",
        });
        return album;
    }

    private static JsonObject SimpleTrack(Track t) => new()
    {
        ["id"] = t.Id,
        ["name"] = t.Name,
        ["type"] = "track",
        ["uri"] = $"spotify:track:{t.Id}",
        ["href"] = "",
        ["duration_ms"] = t.DurationMs,
        ["track_number"] = t.Number,
        ["disc_number"] = 1,
        ["explicit"] = false,
        ["is_playable"] = true,
        ["is_local"] = false,
        ["preview_url"] = null,
        ["artists"] = new JsonArray(SimpleArtist()),
        ["external_urls"] = ExternalUrls(),
    };

    private static JsonObject FullTrack(Track t)
    {
        var track = SimpleTrack(t);
        track["album"] = SimpleAlbum(AlbumOf(t));
        track["popularity"] = 50;
        track["external_ids"] = new JsonObject();
        return track;
    }

    private static JsonObject Owner() => new()
    {
        ["id"] = ProfileId,
        ["display_name"] = "Demo listener",
        ["type"] = "user",
        ["uri"] = $"spotify:user:{ProfileId}",
        ["href"] = "",
        ["external_urls"] = new JsonObject(),
    };

    private static JsonObject SimplePlaylist(Playlist p) => new()
    {
        ["id"] = p.Id,
        ["name"] = p.Name,
        ["description"] = p.Description,
        ["type"] = "playlist",
        ["uri"] = $"spotify:playlist:{p.Id}",
        ["href"] = "",
        ["images"] = Images(p.Cover),
        ["owner"] = Owner(),
        ["public"] = true,
        ["collaborative"] = false,
        ["snapshot_id"] = "demo",
        ["tracks"] = new JsonObject { ["total"] = p.TrackIds.Length, ["href"] = "" },
        ["items"] = new JsonObject { ["total"] = p.TrackIds.Length, ["href"] = "" },
        ["external_urls"] = new JsonObject(),
    };

    private static JsonObject PlaylistItems(Playlist p, int offset, int limit) =>
        Paging(p.TrackIds.Select((id, i) => (JsonNode)new JsonObject
        {
            ["added_at"] = DateTime.UtcNow.AddDays(-10 + i).ToString("o"),
            ["added_by"] = Owner(),
            ["is_local"] = false,
            ["item"] = FullTrack(TrackById(id)),
        }).ToList(), offset, limit);

    private static JsonObject FullPlaylist(Playlist p)
    {
        var playlist = SimplePlaylist(p);
        playlist["followers"] = new JsonObject { ["total"] = 0, ["href"] = null };
        playlist["items"] = PlaylistItems(p, 0, 100);
        playlist["tracks"] = PlaylistItems(p, 0, 100);
        return playlist;
    }

    private static JsonObject Paging(List<JsonNode> all, int offset, int limit)
    {
        offset = Math.Max(0, offset);
        limit = Math.Max(1, limit);

        return new JsonObject
        {
            ["href"] = "",
            ["items"] = new JsonArray(all.Skip(offset).Take(limit).ToArray()),
            ["limit"] = limit,
            ["offset"] = offset,
            ["total"] = all.Count,
            ["next"] = null,
            ["previous"] = null,
        };
    }

    // ---------------------------------------------------------
    // HTTP helpers
    // ---------------------------------------------------------

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static HttpResponseMessage Error(HttpStatusCode status, string message) => new(status)
    {
        Content = new StringContent(
            new JsonObject { ["error"] = new JsonObject { ["status"] = (int)status, ["message"] = message } }.ToJsonString(),
            Encoding.UTF8, "application/json"),
    };
}

// Sends Spotify Web API calls to the demo library while demo mode is on, and keeps demo
// visits out of the server's telemetry. Everything else goes out as usual.
public class DemoSpotifyHandler : DelegatingHandler
{
    private readonly DemoLibrary _demo;

    public DemoSpotifyHandler(DemoLibrary demo) => _demo = demo;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_demo.IsOn && request.RequestUri is { } uri)
        {
            if (uri.Host == "api.spotify.com")
            {
                var response = await _demo.HandleAsync(request);
                response.RequestMessage = request;
                return response;
            }

            if (uri.AbsolutePath.StartsWith("/api/telemetry", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
