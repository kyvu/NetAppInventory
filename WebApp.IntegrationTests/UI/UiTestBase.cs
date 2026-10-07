using WebApp.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using NUnit.Framework.Interfaces;

namespace WebApp.IntegrationTests.UI;

/// <summary>
/// Fresh browser context per test (clean cookies/session), JS console error capture,
/// and a Playwright trace + screenshot saved when a test fails.
/// Open a trace with:  pwsh bin\Debug\net10.0\playwright.ps1 show-trace <file.zip>
/// </summary>
public abstract class UiTestBase
{
    protected IBrowserContext Context = null!;
    protected IPage Page = null!;
    protected readonly List<string> ConsoleErrors = new();

    [SetUp]
    public async Task NewPage()
    {
        Context = await BrowserHost.Browser.NewContextAsync(new()
        {
            BaseURL = TestConfig.Current.BaseUrl,
            IgnoreHTTPSErrors = true,   // IIS Express dev certificate
            ViewportSize = new() { Width = 1600, Height = 1000 },
        });
        await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });

        Page = await Context.NewPageAsync();
        ConsoleErrors.Clear();
        Page.Console += (_, msg) => { if (msg.Type == "error") ConsoleErrors.Add(msg.Text); };
        Page.PageError += (_, err) => ConsoleErrors.Add(err);
    }

    [TearDown]
    public async Task SaveArtifactsOnFailure()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        var dir = Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(dir);
        var name = string.Concat(TestContext.CurrentContext.Test.Name.Split(Path.GetInvalidFileNameChars()));

        if (failed)
        {
            var shot = Path.Combine(dir, $"{name}.png");
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
            TestContext.AddTestAttachment(shot);

            var trace = Path.Combine(dir, $"{name}-trace.zip");
            await Context.Tracing.StopAsync(new() { Path = trace });
            TestContext.AddTestAttachment(trace);
        }
        else
        {
            await Context.Tracing.StopAsync();
        }

        await Context.CloseAsync();
    }

    protected static string Sel(string key)
    {
        if (!TestConfig.Current.Selectors.TryGetValue(key, out var sel) || string.IsNullOrWhiteSpace(sel))
            Assert.Ignore($"Set Selectors:{key} in testsettings.json to run this test.");
        return sel!;
    }

    protected static string PageUrlOrIgnore(string key)
    {
        TestConfig.Current.UiPages.TryGetValue(key, out var url);
        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore($"Set UiPages:{key} in testsettings.json to run this test.");
        return url!;
    }
}
