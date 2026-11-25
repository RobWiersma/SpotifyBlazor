using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBlazor.Client.Pages;
using SpotifyBlazor.Client.Services;
using SpotifyBlazor.Client.Shared;
using SpotifyBlazor.Shared.Models;
using HomePage = SpotifyBlazor.Client.Pages.Index;   // "Index" alone clashes with System.Index

namespace SpotifyBlazor.Tests;

// Pages as a visitor sees them, signed out or in demo mode. Uses the real auth service and demo
// library; bUnit stands in for the browser's JavaScript (localStorage) and navigation, and the
// network is a stub, so no Spotify account or server is needed.
public class PageTests : BunitContext
{
    private readonly SpotifyAuthService _auth;
    private readonly ConfigService _config;

    public PageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;   // localStorage calls just succeed

        var http = new HttpClient(new StubNetwork()) { BaseAddress = new Uri("https://localhost/") };
        var demo = new DemoLibrary(JSInterop.JSRuntime);
        _auth = new SpotifyAuthService(http, JSInterop.JSRuntime, NullLogger<SpotifyAuthService>.Instance,
            new NotificationService(), demo);

        Services.AddSingleton(http);
        Services.AddSingleton(demo);
        Services.AddSingleton(_auth);
        Services.AddSingleton(_config = new ConfigService(http));
    }

    private string CurrentPath => new Uri(Services.GetRequiredService<NavigationManager>().Uri).AbsolutePath;

    // --- Home ---

    [Fact]
    public void Home_shows_Now_Playing_as_the_first_card()
    {
        var cut = Render<HomePage>();

        var cards = cut.FindAll("a.media-card");
        Assert.Equal("/nowplaying", cards[0].GetAttribute("href"));
        Assert.Equal("Now Playing", cut.FindAll(".media-card-title")[0].TextContent);
    }

    [Fact]
    public void Home_offers_the_demo_when_signed_out()
    {
        var cut = Render<HomePage>();

        Assert.Contains("Try the demo", cut.Find(".demo-intro button").TextContent);
        Assert.DoesNotContain("Visitor stats", cut.Markup);   // owner-only card stays hidden
    }

    [Fact]
    public void Trying_the_demo_from_home_turns_demo_mode_on()
    {
        var cut = Render<HomePage>();

        cut.Find(".demo-intro button").Click();

        Assert.True(_auth.IsDemo);
        Assert.Equal(LoginState.LoggedIn, _auth.State);
        Assert.Equal("/", CurrentPath);
        cut.WaitForAssertion(() => Assert.Contains("You're in demo mode", cut.Markup));
    }

    [Fact]
    public async Task Home_in_demo_mode_explains_the_demo_and_credits_the_music()
    {
        await _auth.StartDemoAsync();

        var cut = Render<HomePage>();

        Assert.Contains("You're in demo mode", cut.Markup);
        Assert.Contains("Kevin MacLeod", cut.Markup);
        Assert.DoesNotContain("Try the demo", cut.Markup);
    }

    // --- About ---

    [Fact]
    public void About_works_signed_out_and_links_the_source()
    {
        var cut = Render<About>();

        Assert.Equal("About SpotifyBlazor", cut.Find("h2").TextContent);
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "https://github.com/RobWiersma/SpotifyBlazor");
        Assert.Contains("isn't affiliated with or endorsed by Spotify", cut.Markup);
        Assert.Contains("Try the demo", cut.Find("button").TextContent);
    }

    [Fact]
    public async Task About_hides_the_demo_button_once_in_demo_mode()
    {
        await _auth.StartDemoAsync();

        var cut = Render<About>();

        Assert.Empty(cut.FindAll("button"));
    }

    // --- Header ---

    [Fact]
    public void Header_has_a_real_About_link_search_engines_can_follow()
    {
        var cut = Render<SpotHeader>();

        Assert.Equal("/about", cut.Find("a.about-link").GetAttribute("href"));
    }

    [Fact]
    public async Task Header_shows_demo_mode_with_a_way_out()
    {
        await _config.LoadAsync();   // until the config loads, the header only says "Loading config…"
        await _auth.StartDemoAsync();

        var cut = Render<SpotHeader>();

        Assert.Contains("Demo mode", cut.Find(".demo-badge").TextContent);
        Assert.Contains("Exit demo", cut.Find(".demo-badge button").TextContent);
    }

    // Answers every request with an empty 200, so nothing reaches the internet
    private class StubNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
    }
}
