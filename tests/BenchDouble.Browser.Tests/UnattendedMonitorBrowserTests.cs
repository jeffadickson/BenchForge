using Microsoft.Playwright;
using Xunit;

namespace BenchDouble.Browser.Tests;

/// <summary>
/// End-to-end DOM and SVG smoke tests. These complement—not replace—the C#
/// unit and Reqnroll acceptance tests of the actual monitor logic.
/// </summary>
public sealed class UnattendedMonitorBrowserTests
{
    [Fact]
    public async Task InjectedFaultShowsAlarmAndSimulatedNotifications()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true
        });
        var page = await browser.NewPageAsync();
        var html = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../examples/UnattendedOperationsMonitor/index.html"));
        // Locate from repository root even when tests run in nested output folders.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName,
                   "examples/UnattendedOperationsMonitor/index.html")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        await page.GotoAsync(new Uri(Path.Combine(dir!.FullName,
            "examples/UnattendedOperationsMonitor/index.html")).AbsoluteUri);
        Assert.Contains("Unattended Operations Monitor", await page.TitleAsync());
        Assert.Equal("0.50", await page.Locator("#current").InnerTextAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Inject fault now" }).ClickAsync();
        await page.Locator("#status.alarm").WaitForAsync(new() { Timeout = 10000 });
        var log = await page.Locator("#log").InnerTextAsync();
        Assert.Contains("ALARM:", log);
        Assert.Contains("SIMULATED EMAIL", log);
        Assert.Contains("SIMULATED SMS", log);
        Assert.Contains("not actually sent", log);
        Assert.True(await page.Locator("#plot polyline").CountAsync() >= 1);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        Assert.Equal("0.50", await page.Locator("#current").InnerTextAsync());
        Assert.DoesNotContain("ALARM:", await page.Locator("#log").InnerTextAsync());
    }

    [Fact]
    public async Task DisabledNotificationsDoNotChangeOvercurrentDetection()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName,
                   "examples/UnattendedOperationsMonitor/index.html")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        await page.GotoAsync(new Uri(Path.Combine(dir!.FullName,
            "examples/UnattendedOperationsMonitor/index.html")).AbsoluteUri);
        await page.Locator("#email").UncheckAsync();
        await page.Locator("#sms").UncheckAsync();
        await page.Locator("#fault").ClickAsync();
        await page.Locator("#status.alarm").WaitForAsync(new() { Timeout = 10000 });
        var log = await page.Locator("#log").InnerTextAsync();
        Assert.Contains("ALARM:", log);
        Assert.DoesNotContain("SIMULATED EMAIL:", log);
        Assert.DoesNotContain("SIMULATED SMS:", log);
    }
}
