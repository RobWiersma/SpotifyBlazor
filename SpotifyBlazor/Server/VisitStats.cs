using System.Text.Json;
using System.Text.RegularExpressions;
using SpotifyBlazor.Shared.Models;

// Visitor stats for the owner's /stats page: who visits, and whether they're signed in with
// Spotify, trying the demo, or just looking. Kept per day in memory and saved to a small JSON
// file in the App Service's persistent storage, so no database or extra Azure resource is needed.
//
// What's stored per visitor and day: a random id the browser made up (no IP address), the mode,
// page views, first and last seen, and for signed-in users the Spotify id and display name
// taken from our own login token (so they can't be faked).
//
// The owner's own visits aren't counted: signed in as an account in Stats:OwnerSpotifyIds, or
// from a browser the owner has signed in on (remembered in a second small file), even signed out.
public class VisitStats : IHostedService, IDisposable
{
    private const int KeepDays = 90;

    private const int MaxOwnerBrowsers = 100;

    private readonly string _file;
    private readonly string _ownerFile;
    private readonly HashSet<string> _owners;
    private readonly HashSet<string> _ownerBrowsers = new();   // visitor ids the owner signed in on
    private readonly ILogger<VisitStats> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, DayStats> _days = new();   // "yyyy-MM-dd" (UTC) → that day
    private Timer? _saveTimer;
    private bool _dirty;

    public VisitStats(IWebHostEnvironment env, ILogger<VisitStats> logger, IConfiguration? config = null)
    {
        _logger = logger;
        _owners = OwnerIds(config).ToHashSet();

        // On Azure App Service, %HOME% (D:\home) survives restarts and deploys, and
        // wwwroot may be read-only (run from package); locally use App_Data
        var home = Environment.GetEnvironmentVariable("HOME");
        var dir = !string.IsNullOrEmpty(home) && Directory.Exists(Path.Combine(home, "site"))
            ? Path.Combine(home, "data", "spotifyblazor")
            : Path.Combine(env.ContentRootPath, "App_Data");

        _file = Path.Combine(dir, "visit-stats.json");
        _ownerFile = Path.Combine(dir, "owner-browsers.json");
    }

    // The Spotify accounts in Stats:OwnerSpotifyIds (comma-separated): they see the stats, and
    // their own visits aren't counted
    public static IEnumerable<string> OwnerIds(IConfiguration? config) =>
        (config?["Stats:OwnerSpotifyIds"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---------------------------------------------------------
    // Recording
    // ---------------------------------------------------------

    public void Record(VisitRequest visit, string? spotifyId, string? spotifyName)
    {
        if (string.IsNullOrWhiteSpace(visit.VisitorId) || visit.VisitorId.Length > 64)
            return;

        // A valid login token wins over what the browser says
        var mode = spotifyId != null ? VisitModes.Spotify
            : visit.Mode is VisitModes.Demo or VisitModes.SignedOut ? visit.Mode
            : VisitModes.SignedOut;

        var now = DateTime.UtcNow;
        var key = now.ToString("yyyy-MM-dd");
        var page = NormalizePath(visit.Path);

        lock (_lock)
        {
            // The owner's visits don't count. A browser they sign in on stays left out when
            // signed out or in demo mode too, and what it counted earlier is taken back out.
            if (spotifyId != null && _owners.Contains(spotifyId))
            {
                if (_ownerBrowsers.Count < MaxOwnerBrowsers && _ownerBrowsers.Add(visit.VisitorId))
                {
                    ForgetOwnerVisits();
                    _dirty = true;
                }
                return;
            }

            if (_ownerBrowsers.Contains(visit.VisitorId))
                return;

            if (!_days.TryGetValue(key, out var day))
                _days[key] = day = new DayStats();

            // Some ids used per day are bounded, so a flood of made-up ids can't grow the file forever
            if (!day.Visitors.TryGetValue(visit.VisitorId, out var v))
            {
                if (day.Visitors.Count >= 5000)
                    return;

                day.Visitors[visit.VisitorId] = v = new VisitorDay { First = now };
            }

            v.Last = now;
            v.PageViews++;
            v.Mode = mode;

            if (spotifyId != null)
            {
                v.SpotifyId = spotifyId;
                v.SpotifyName = string.IsNullOrWhiteSpace(spotifyName) ? null : spotifyName;
            }

            if (page != null && (day.Pages.ContainsKey(page) || day.Pages.Count < 200))
                day.Pages[page] = day.Pages.GetValueOrDefault(page) + 1;

            _dirty = true;
        }
    }

    // Removes visitors that were the owner from every day kept (call inside the lock). Their
    // page counts can't be told apart from everyone else's, so those stay.
    private void ForgetOwnerVisits()
    {
        foreach (var day in _days.Values)
            foreach (var (id, v) in day.Visitors.ToList())
                if (_ownerBrowsers.Contains(id) || (v.SpotifyId != null && _owners.Contains(v.SpotifyId)))
                    day.Visitors.Remove(id);
    }

    // "/album/4aawyAB9vmqN3uQ7FjRGTy?x=1" → "/album/{id}", so pages group and ids aren't kept
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var p = path.Split('?', '#')[0].ToLowerInvariant();
        if (p.Length > 100)
            return null;

        p = Regex.Replace(p, "^/(album|artist|playlist|show)/[^/]+", "/$1/{id}");
        return p == "" ? "/" : p;
    }

    // ---------------------------------------------------------
    // Summary for the stats page
    // ---------------------------------------------------------

    public StatsSummary Summarize()
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.Date;
            var days = Enumerable.Range(0, 30).Select(i => today.AddDays(-i)).Reverse()
                .Select(d => (Date: d, Stats: _days.GetValueOrDefault(d.ToString("yyyy-MM-dd"))))
                .ToList();

            StatsPeriod Period(int n)
            {
                var visitors = days.TakeLast(n).Where(d => d.Stats != null)
                    .SelectMany(d => d.Stats!.Visitors)
                    .GroupBy(v => v.Key)
                    .Select(g => g.OrderBy(x => x.Value.Last).Last().Value)  // latest mode wins
                    .ToList();

                return new StatsPeriod
                {
                    Visitors = visitors.Count,
                    Spotify = visitors.Count(v => v.Mode == VisitModes.Spotify),
                    Demo = visitors.Count(v => v.Mode == VisitModes.Demo),
                    SignedOut = visitors.Count(v => v.Mode == VisitModes.SignedOut),
                    PageViews = days.TakeLast(n).Where(d => d.Stats != null)
                        .Sum(d => d.Stats!.Visitors.Values.Sum(v => v.PageViews)),
                };
            }

            var users = days.Where(d => d.Stats != null)
                .SelectMany(d => d.Stats!.Visitors.Values.Where(v => v.SpotifyId != null).Select(v => (d.Date, v)))
                .GroupBy(x => x.v.SpotifyId!)
                .Select(g => new StatsUser
                {
                    SpotifyId = g.Key,
                    Name = g.OrderBy(x => x.v.Last).Last().v.SpotifyName ?? g.Key,
                    DaysActive = g.Select(x => x.Date).Distinct().Count(),
                    PageViews = g.Sum(x => x.v.PageViews),
                    LastSeen = g.Max(x => x.v.Last),
                })
                .OrderByDescending(u => u.LastSeen)
                .ToList();

            var pages = days.Where(d => d.Stats != null)
                .SelectMany(d => d.Stats!.Pages)
                .GroupBy(p => p.Key)
                .Select(g => new StatsPage { Path = g.Key, Views = g.Sum(p => p.Value) })
                .OrderByDescending(p => p.Views)
                .Take(15)
                .ToList();

            return new StatsSummary
            {
                Today = Period(1),
                Last7Days = Period(7),
                Last30Days = Period(30),
                Daily = days.Select(d => new StatsDay
                {
                    Date = d.Date.ToString("yyyy-MM-dd"),
                    Spotify = d.Stats?.Visitors.Values.Count(v => v.Mode == VisitModes.Spotify) ?? 0,
                    Demo = d.Stats?.Visitors.Values.Count(v => v.Mode == VisitModes.Demo) ?? 0,
                    SignedOut = d.Stats?.Visitors.Values.Count(v => v.Mode == VisitModes.SignedOut) ?? 0,
                }).ToList(),
                SignedInUsers = users,
                TopPages = pages,
            };
        }
    }

    // ---------------------------------------------------------
    // Saving: loaded at start, written every 30 seconds when something changed, and at shutdown
    // ---------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_file))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, DayStats>>(File.ReadAllText(_file));
                if (saved != null)
                    foreach (var (k, v) in saved) _days[k] = v;
            }

            if (File.Exists(_ownerFile))
                _ownerBrowsers.UnionWith(JsonSerializer.Deserialize<string[]>(File.ReadAllText(_ownerFile)) ?? []);

            // Browsers the owner signed in on before this was added are in the saved days
            foreach (var day in _days.Values)
                foreach (var (id, v) in day.Visitors)
                    if (v.SpotifyId != null && _owners.Contains(v.SpotifyId) && _ownerBrowsers.Count < MaxOwnerBrowsers)
                        _ownerBrowsers.Add(id);

            var before = _days.Values.Sum(d => d.Visitors.Count);
            ForgetOwnerVisits();
            _dirty = _days.Values.Sum(d => d.Visitors.Count) != before;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't read visit stats from {File}; starting fresh", _file);
        }

        _saveTimer = new Timer(_ => Save(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        Save();
        return Task.CompletedTask;
    }

    private void Save()
    {
        string json, ownerJson;

        lock (_lock)
        {
            if (!_dirty)
                return;

            var cutoff = DateTime.UtcNow.Date.AddDays(-KeepDays).ToString("yyyy-MM-dd");
            foreach (var old in _days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
                _days.Remove(old);

            json = JsonSerializer.Serialize(_days);
            ownerJson = JsonSerializer.Serialize(_ownerBrowsers);
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _file, overwrite: true);   // never leaves a half-written file
            File.WriteAllText(temp, ownerJson);
            File.Move(temp, _ownerFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't save visit stats to {File}", _file);
            lock (_lock) _dirty = true;
        }
    }

    public void Dispose() => _saveTimer?.Dispose();

    private class DayStats
    {
        public Dictionary<string, VisitorDay> Visitors { get; set; } = new();
        public Dictionary<string, int> Pages { get; set; } = new();
    }

    private class VisitorDay
    {
        public string Mode { get; set; } = VisitModes.SignedOut;
        public string? SpotifyId { get; set; }
        public string? SpotifyName { get; set; }
        public int PageViews { get; set; }
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
    }
}
