namespace SpotifyBlazor.Shared.Models;

// Visitor stats: what the browser reports on each page view, and the owner's summary

public static class VisitModes
{
    public const string Spotify = "spotify";      // signed in with Spotify
    public const string Demo = "demo";            // trying demo mode
    public const string SignedOut = "signed-out"; // just looking
}

public class VisitRequest
{
    public string VisitorId { get; set; } = "";   // random id this browser keeps; no personal data
    public string Mode { get; set; } = VisitModes.SignedOut;
    public string? Path { get; set; }
}

public class StatsAccess
{
    public bool IsOwner { get; set; }
    public string? SpotifyId { get; set; }        // shown when you aren't the owner yet, to set it up
}

public class StatsSummary
{
    public StatsPeriod Today { get; set; } = new();
    public StatsPeriod Last7Days { get; set; } = new();
    public StatsPeriod Last30Days { get; set; } = new();
    public List<StatsDay> Daily { get; set; } = new();          // last 30 days, oldest first (UTC)
    public List<StatsUser> SignedInUsers { get; set; } = new();
    public List<StatsPage> TopPages { get; set; } = new();
}

public class StatsPeriod
{
    public int Visitors { get; set; }
    public int Spotify { get; set; }
    public int Demo { get; set; }
    public int SignedOut { get; set; }
    public int PageViews { get; set; }
}

public class StatsDay
{
    public string Date { get; set; } = "";
    public int Spotify { get; set; }
    public int Demo { get; set; }
    public int SignedOut { get; set; }
}

public class StatsUser
{
    public string SpotifyId { get; set; } = "";
    public string Name { get; set; } = "";
    public int DaysActive { get; set; }
    public int PageViews { get; set; }
    public DateTime LastSeen { get; set; }
}

public class StatsPage
{
    public string Path { get; set; } = "";
    public int Views { get; set; }
}
