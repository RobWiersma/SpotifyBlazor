using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBlazor.Shared.Models;

namespace SpotifyBlazor.Tests;

// The owner's visitor stats: who counts as what, how pages group, and the limits that keep
// made-up ids from growing the stats file. Only in memory: nothing here saves to disk.
public class VisitStatsTests
{
    private readonly VisitStats _stats = new(new TestEnvironment(), NullLogger<VisitStats>.Instance);

    private void Visit(string visitor, string mode = VisitModes.SignedOut, string? path = "/",
        string? spotifyId = null, string? spotifyName = null) =>
        _stats.Record(new VisitRequest { VisitorId = visitor, Mode = mode, Path = path }, spotifyId, spotifyName);

    [Fact]
    public void A_valid_login_token_wins_over_the_mode_the_browser_reports()
    {
        Visit("a", mode: VisitModes.Demo, spotifyId: "user1", spotifyName: "Pat");

        var today = _stats.Summarize().Today;
        Assert.Equal(1, today.Spotify);
        Assert.Equal(0, today.Demo);
    }

    [Fact]
    public void A_browser_cant_claim_to_be_signed_in_without_a_token()
    {
        Visit("a", mode: VisitModes.Spotify);
        Visit("b", mode: "made-up");

        var today = _stats.Summarize().Today;
        Assert.Equal(0, today.Spotify);
        Assert.Equal(2, today.SignedOut);
    }

    [Fact]
    public void The_same_visitor_counts_once_and_their_latest_mode_wins()
    {
        Visit("a", mode: VisitModes.SignedOut);
        Visit("a", mode: VisitModes.Demo);

        var today = _stats.Summarize().Today;
        Assert.Equal(1, today.Visitors);
        Assert.Equal(1, today.Demo);
        Assert.Equal(0, today.SignedOut);
        Assert.Equal(2, today.PageViews);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Visits_without_a_visitor_id_are_ignored(string id)
    {
        Visit(id);

        Assert.Equal(0, _stats.Summarize().Today.Visitors);
    }

    [Fact]
    public void Visitor_ids_longer_than_64_characters_are_ignored()
    {
        Visit(new string('x', 65));
        Visit(new string('y', 64));

        Assert.Equal(1, _stats.Summarize().Today.Visitors);
    }

    [Fact]
    public void A_day_keeps_at_most_5000_visitors()
    {
        for (var i = 0; i < 5001; i++)
            Visit($"visitor-{i}");

        Assert.Equal(5000, _stats.Summarize().Today.Visitors);
    }

    [Theory]
    [InlineData("/album/4aawyAB9vmqN3uQ7FjRGTy?x=1", "/album/{id}")]
    [InlineData("/artist/abc", "/artist/{id}")]
    [InlineData("/playlist/abc#top", "/playlist/{id}")]
    [InlineData("/show/abc", "/show/{id}")]
    [InlineData("/Liked", "/liked")]
    [InlineData("/", "/")]
    public void Pages_group_without_ids_or_query_strings(string path, string expected)
    {
        Visit("a", path: path);

        Assert.Equal(expected, Assert.Single(_stats.Summarize().TopPages).Path);
    }

    [Fact]
    public void Very_long_paths_are_not_recorded_but_the_visit_counts()
    {
        Visit("a", path: "/" + new string('a', 120));

        var summary = _stats.Summarize();
        Assert.Empty(summary.TopPages);
        Assert.Equal(1, summary.Today.PageViews);
    }

    [Fact]
    public void Signed_in_users_are_listed_by_their_latest_name()
    {
        Visit("a", spotifyId: "user1", spotifyName: "Old name");
        Visit("a", spotifyId: "user1", spotifyName: "New name");

        var user = Assert.Single(_stats.Summarize().SignedInUsers);
        Assert.Equal("user1", user.SpotifyId);
        Assert.Equal("New name", user.Name);
        Assert.Equal(2, user.PageViews);
        Assert.Equal(1, user.DaysActive);
    }

    [Fact]
    public void The_daily_chart_covers_30_days_ending_today()
    {
        Visit("a", mode: VisitModes.Demo);

        var daily = _stats.Summarize().Daily;
        Assert.Equal(30, daily.Count);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), daily[^1].Date);
        Assert.Equal(1, daily[^1].Demo);
    }

    private class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "SpotifyBlazor.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}
