using Invora.Desktop;
using Xunit;

namespace Invora.UnitTests;
public sealed class DesktopNavigationTests
{
    [Fact]
    public void LocalOriginRejectsRemoteCredentialsPathsAndInvalidPorts()
    {
        Assert.True(NavigationPolicy.TryLocalOrigin("http://127.0.0.1:8080", out var origin));
        Assert.Equal(8080, origin!.Port);
        foreach (var invalid in new[] { "https://shop.test", "file:///tmp/shop", "http://0.0.0.0:8080", "http://127.0.0.1:99999", "http://user:password@localhost:8080", "http://localhost:8080/sales", "http://localhost:8080/?key=value" })
            Assert.False(NavigationPolicy.TryLocalOrigin(invalid, out _));
    }
    [Fact]
    public void WorkspaceAndBlobDownloadsStayOnTheConfiguredOrigin()
    {
        var origin = new Uri("http://127.0.0.1:8080");
        Assert.True(NavigationPolicy.IsInternal(origin, "http://127.0.0.1:8080/sales"));
        Assert.True(NavigationPolicy.IsInternal(origin, "blob:http://127.0.0.1:8080/fixture"));
        foreach (var invalid in new[] { "http://127.0.0.1:9090/sales", "http://localhost:8080", "http://127.0.0.1.evil.test:8080", "blob:https://other.test/fixture", "blob:blob:http://127.0.0.1:8080/fixture", "file:///tmp/invoice.pdf", "javascript:alert(1)" })
            Assert.False(NavigationPolicy.IsInternal(origin, invalid));
        Assert.True(NavigationPolicy.IsExternal("https://wa.me/910000000000"));
        Assert.True(NavigationPolicy.IsExternal("tel:+910000000000"));
        Assert.True(NavigationPolicy.IsExternal("sms:+910000000000?body=Reminder"));
        Assert.False(NavigationPolicy.IsExternal("file:///tmp/a.exe"));
        Assert.False(NavigationPolicy.IsExternal("javascript:alert(1)"));
    }
}
