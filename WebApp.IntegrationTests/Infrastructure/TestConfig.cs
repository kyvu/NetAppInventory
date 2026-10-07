using System.Text.Json;

namespace WebApp.IntegrationTests.Infrastructure;

public sealed class TestConfig
{
    public string BaseUrl { get; init; } = "https://localhost:44300";
    public string SqlConnection { get; init; } = "";
    public string AppManifest { get; init; } = "manifests/manifest-app.json";
    public string SqlManifest { get; init; } = "manifests/manifest-sql.json";
    public string BrowserChannel { get; init; } = "msedge";
    public bool Headless { get; init; } = true;
    public int SlowMoMs { get; init; }
    public Dictionary<string, string> UiPages { get; init; } = new();
    public Dictionary<string, string> Selectors { get; init; } = new();

    static readonly Lazy<TestConfig> _current = new(Load);
    public static TestConfig Current => _current.Value;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string PathInOutput(string relative) =>
        Path.Combine(AppContext.BaseDirectory, relative);

    static TestConfig Load()
    {
        var path = PathInOutput("testsettings.json");
        var cfg = File.Exists(path)
            ? JsonSerializer.Deserialize<TestConfig>(File.ReadAllText(path), Json) ?? new TestConfig()
            : new TestConfig();

        // Allow overrides from environment (handy later for DevOps): TEST_BASEURL, TEST_SQL
        return new TestConfig
        {
            BaseUrl = Environment.GetEnvironmentVariable("TEST_BASEURL") ?? cfg.BaseUrl,
            SqlConnection = Environment.GetEnvironmentVariable("TEST_SQL") ?? cfg.SqlConnection,
            AppManifest = cfg.AppManifest,
            SqlManifest = cfg.SqlManifest,
            BrowserChannel = cfg.BrowserChannel,
            Headless = cfg.Headless,
            SlowMoMs = cfg.SlowMoMs,
            UiPages = cfg.UiPages,
            Selectors = cfg.Selectors,
        };
    }
}
