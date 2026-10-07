using System.Net;
using WebApp.IntegrationTests.Infrastructure;

namespace WebApp.IntegrationTests.Smoke;

/// <summary>
/// One test per GET action in the manifest. Fails on 5xx, 401/403 (auth not flowing),
/// or redirects to an error page. Fast – no browser.
/// </summary>
[TestFixture, Category("Smoke")]
public class RouteSmokeTests
{
    HttpClient _client = null!;

    [OneTimeSetUp]
    public void Setup() => _client = HttpClientFactory.Create();

    [OneTimeTearDown]
    public void TearDown()
    {
        _client.Dispose();
        if (ManifestRoutes.Skipped.Count > 0)
            TestContext.Progress.WriteLine("Skipped routes (add samples in route-samples.json to cover them):\n  "
                                           + string.Join("\n  ", ManifestRoutes.Skipped));
    }

    static IEnumerable<TestCaseData> Routes() =>
        ManifestRoutes.GetRoutes().Select(r => new TestCaseData(r.Url).SetName($"GET {r.Key}"));

    [TestCaseSource(nameof(Routes))]
    public async Task Get_DoesNotError(string url)
    {
        using var res = await _client.GetAsync(url);
        var code = (int)res.StatusCode;

        Assert.That(res.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
            $"{url} returned 401 – Windows auth isn't reaching the app (check IIS Express windowsAuthentication).");

        Assert.That(code, Is.LessThan(500), $"{url} returned {code}:\n{await Snippet(res)}");

        if (code is >= 300 and < 400)
        {
            var location = res.Headers.Location?.ToString() ?? "";
            Assert.That(location, Does.Not.Contain("Error").IgnoreCase, $"{url} redirected to an error page: {location}");
        }
    }

    static async Task<string> Snippet(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        return body.Length > 1500 ? body[..1500] + "..." : body;
    }
}
