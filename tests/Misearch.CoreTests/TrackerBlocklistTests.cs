using Xunit;

using Misearch.Core.Models;
using Misearch.Core.Services;

namespace Misearch.CoreTests;

public sealed class TrackerBlocklistTests
{
    [Fact]
    public void The_embedded_list_loads()
    {
        var blocklist = new TrackerBlocklist();
        blocklist.Load();

        Assert.True(blocklist.Count > 0);
    }

    [Theory]
    [InlineData("doubleclick.net")]
    [InlineData("ad.doubleclick.net")]
    [InlineData("sub.ad.doubleclick.net.")]
    public void Known_trackers_are_blocked_including_subdomains(string host)
    {
        var blocklist = new TrackerBlocklist();
        blocklist.Load();

        Assert.True(blocklist.IsBlocked(host));
    }

    [Theory]
    [InlineData("ejemplo.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ordinary_hosts_are_not_blocked(string host)
    {
        var blocklist = new TrackerBlocklist();
        blocklist.Load();

        Assert.False(blocklist.IsBlocked(host));
    }
}
