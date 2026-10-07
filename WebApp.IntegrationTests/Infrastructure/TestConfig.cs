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

    /// Prefers the file in the project folder (so a fresh AppInventory run or a settings edit
    /// is picked up without rebuilding), falling back to the copy in the build output.
    public static string PathInOutput(string relative)
    {
        if (ProjectDir != null)
        {
            var source = Path.Combine(ProjectDir, relative);
            if (File.Exists(source)) return source;
        }
        return Path.Combine(AppContext.BaseDirectory, relative);
    }

    static readonly string? ProjectDir = FindProjectDir();

    static string? FindProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (dir.GetFiles("*.IntegrationTests.csproj").Length > 0) return dir.FullName;
        return null;
    }

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
