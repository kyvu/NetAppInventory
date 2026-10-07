using WebApp.IntegrationTests.Infrastructure;
using Microsoft.Playwright;

namespace WebApp.IntegrationTests.UI;

/// <summary>
/// Launches ONE browser for all UI tests in this namespace and closes it at the end.
/// Uses the installed Edge (Channel = "msedge"), so no Playwright browser download is needed.
/// </summary>
[SetUpFixture]
public class BrowserHost
{
    public static IBrowser Browser { get; private set; } = null!;
    static IPlaywright _playwright = null!;

    [OneTimeSetUp]
    public async Task Launch()
    {
        var cfg = TestConfig.Current;
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = cfg.BrowserChannel,
            Headless = cfg.Headless,
            SlowMo = cfg.SlowMoMs,
            // Lets Edge send your Windows credentials to the local site automatically.
            Args = new[] { "--auth-server-allowlist=localhost,127.0.0.1" },
        });
    }

    [OneTimeTearDown]
    public async Task Close()
    {
        await Browser.CloseAsync();
        _playwright.Dispose();
    }
}
