using Bunit;
using SpotifyBlazor.Client.Shared;

namespace SpotifyBlazor.Tests;

// Renders components the way the browser would (bUnit) and checks the markup and events.
// These two need no services; pages that use SpotifyAuthService need a fake for it first.
public class ComponentTests : BunitContext
{
    [Fact]
    public void LengthFilter_lists_every_option_with_any_length_first()
    {
        var cut = Render<LengthFilter>();

        var options = cut.FindAll("option").Select(o => o.TextContent).ToList();
        Assert.Equal(LengthFilter.Options.Length, options.Count);
        Assert.Equal("any length", options[0]);
        Assert.Contains("1.5 min", options);
        Assert.Equal("30 min", options[^1]);
    }

    [Fact]
    public void LengthFilter_shows_the_picked_value_and_highlights_when_on()
    {
        var cut = Render<LengthFilter>(p => p.Add(x => x.Minutes, 1.5));

        var select = cut.Find("select");
        Assert.Equal("1.5", select.GetAttribute("value"));
        Assert.Contains("on", select.ClassList);
    }

    [Fact]
    public void LengthFilter_is_not_highlighted_at_any_length()
    {
        var cut = Render<LengthFilter>(p => p.Add(x => x.Minutes, 0));

        Assert.DoesNotContain("on", cut.Find("select").ClassList);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("10", 10)]
    [InlineData("0", 0)]
    [InlineData("not a number", 0)]   // anything odd falls back to any length
    public void LengthFilter_reports_the_new_minimum(string picked, double expected)
    {
        double? reported = null;
        var cut = Render<LengthFilter>(p => p
            .Add(x => x.Minutes, 3)
            .Add(x => x.MinutesChanged, (double m) => reported = m));

        cut.Find("select").Change(picked);

        Assert.Equal(expected, reported);
    }

    [Fact]
    public void DemoCredit_credits_the_artist_and_links_the_licence()
    {
        var cut = Render<DemoCredit>();

        Assert.Contains("Kevin MacLeod", cut.Markup);
        var links = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains("https://incompetech.com", links);
        Assert.Contains("https://creativecommons.org/licenses/by/4.0/", links);

        // Links open in a new tab without giving that page access to this one
        Assert.All(cut.FindAll("a"), a => Assert.Equal("noopener", a.GetAttribute("rel")));
    }
}
