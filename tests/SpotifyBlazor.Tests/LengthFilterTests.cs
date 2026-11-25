using SpotifyBlazor.Client.Shared;

namespace SpotifyBlazor.Tests;

// The "Longer than" filter used on search, albums, playlists, Liked and history
public class LengthFilterTests
{
    [Theory]
    [InlineData(10_000, 0, true)]          // "any length" lets everything through
    [InlineData(59_999, 1, false)]
    [InlineData(60_000, 1, true)]          // exactly the limit counts as long enough
    [InlineData(89_999, 1.5, false)]
    [InlineData(90_000, 1.5, true)]
    [InlineData(1_800_000, 30, true)]
    public void Passes_tracks_at_least_as_long_as_the_limit(int durationMs, double minutes, bool expected)
    {
        Assert.Equal(expected, LengthFilter.Passes(durationMs, minutes));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5_000, "0:05")]
    [InlineData(90_000, "1:30")]
    [InlineData(599_000, "9:59")]
    [InlineData(3_599_000, "59:59")]
    [InlineData(3_600_000, "1:00:00")]     // long mixes switch to h:mm:ss
    [InlineData(4_501_000, "1:15:01")]
    public void Formats_durations_like_Spotify(int ms, string expected)
    {
        Assert.Equal(expected, LengthFilter.Format(ms));
    }

    [Fact]
    public void Options_start_at_any_length_and_go_up()
    {
        Assert.Equal(0, LengthFilter.Options[0]);
        Assert.Equal(LengthFilter.Options.OrderBy(m => m), LengthFilter.Options);
    }
}
