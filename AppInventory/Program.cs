using System.Text.Json;
using System.Text.Json.Serialization;
using AppInventory;

// Two ways to run:
//
// 1. No arguments (F5 in Visual Studio, or double-click the exe):
//    reads inventorysettings.json and writes manifest-app.json + manifest-sql.json
//    straight into the test project's manifests folder.
//
// 2. Command line (overrides the settings file):
//    AppInventory --assembly <dll> [--views <folder>] [--sql "<connstr>"] [--check-sql-health] [--out manifest.json]

var jsonOut = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

if (args.Length == 0)
    return RunFromSettings();

var opts = ParseArgs(args);
if (opts.ContainsKey("help"))
{
    Console.WriteLine("""
        AppInventory – structure-only manifest for test generation (no data, no secrets)

          (no args)               Use inventorysettings.json
          --assembly <dll>        Web app assembly (.NET 4.8 or .NET 10) in its bin folder
          --views <folder>        Views root to scan .cshtml files
          --sql "<connstr>"       SQL Server connection string (Windows auth recommended)
          --check-sql-health      Also run SELECT TOP 0 on every view to find broken bindings
          --out <file>            Output file (default manifest.json)
        """);
    return 0;
}

var manifest = Build(opts.GetValueOrDefault("assembly"), opts.GetValueOrDefault("views"),
                     opts.GetValueOrDefault("sql"), opts.ContainsKey("check-sql-health"));
Write(opts.GetValueOrDefault("out") ?? "manifest.json", manifest);
return 0;

// ---------------------------------------------------------------------------

int RunFromSettings()
{
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "inventorysettings.json");
    if (!File.Exists(settingsPath))
    {
        Console.WriteLine($"No arguments and no settings file at {settingsPath}. Run with --help.");
        return 1;
    }

    var s = JsonSerializer.Deserialize<InventorySettings>(File.ReadAllText(settingsPath), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    }) ?? new InventorySettings();

    var outDir = Path.GetFullPath(Path.IsPathRooted(s.OutputFolder)
        ? s.OutputFolder
        : Path.Combine(AppContext.BaseDirectory, s.OutputFolder));
    Directory.CreateDirectory(outDir);

    int rc = 0;
    try
    {
        Manifest? current = null, legacy = null;

        if (!string.IsNullOrWhiteSpace(s.Assembly))
        {
            current = Build(s.Assembly, NullIfEmpty(s.Views), null, false);
            Write(Path.Combine(outDir, "manifest-app.json"), current);
        }

        if (!string.IsNullOrWhiteSpace(s.LegacyAssembly))
        {
            Console.WriteLine("\n-- Legacy app --");
            legacy = Build(s.LegacyAssembly, NullIfEmpty(s.LegacyViews), null, false);
            Write(Path.Combine(outDir, "manifest-legacy.json"), legacy);
        }

        if (current != null && legacy != null)
        {
            var reportPath = Path.Combine(outDir, "migration-compare.md");
            var report = CompareReport.Build(legacy, current);
            File.WriteAllText(reportPath, report);
            Console.WriteLine("\n" + string.Join("\n", report.Split('\n').SkipWhile(l => !l.StartsWith("## Summary")).TakeWhile(l => !l.StartsWith("### ")).Where(l => l.Length > 0)));
            foreach (var l in report.Split('\n').Where(l => l.StartsWith("## Views"))) Console.WriteLine(l);
            Console.WriteLine($"Wrote {reportPath}");
        }

        if (!string.IsNullOrWhiteSpace(s.SqlConnection) && !s.SqlConnection.Contains("YOUR_TEST_DB"))
            Write(Path.Combine(outDir, "manifest-sql.json"), Build(null, null, s.SqlConnection, s.CheckSqlHealth));
        else
            Console.WriteLine("SqlConnection not set – skipping SQL scan.");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
        Console.ResetColor();
        rc = 1;
    }

    if (s.PauseAtEnd && !Console.IsInputRedirected)
    {
        Console.WriteLine("\nPress any key to close...");
        Console.ReadKey();
    }
    return rc;
}

static Manifest Build(string? assembly, string? viewsPath, string? sqlConn, bool checkHealth)
{
    AppInfo? app = null;
    ViewInfo? views = null;
    SqlInfo? sql = null;

    if (!string.IsNullOrWhiteSpace(assembly))
    {
        Console.WriteLine($"Scanning controllers in {assembly} ...");
        app = ControllerScanner.Scan(assembly);
        Console.WriteLine($"  {app.Framework}: {app.Controllers.Count} controllers, {app.Controllers.Sum(c => c.Actions.Count)} actions");
    }

    if (!string.IsNullOrWhiteSpace(viewsPath))
    {
        Console.WriteLine($"Scanning views in {viewsPath} ...");
        views = ViewScanner.Scan(viewsPath);
        Console.WriteLine($"  {views.Files.Count} views, {views.Files.Sum(f => f.Warnings.Count)} warnings");
    }

    if (!string.IsNullOrWhiteSpace(sqlConn))
    {
        Console.WriteLine("Scanning SQL schema ...");
        sql = SqlScanner.Scan(sqlConn, checkHealth);
        Console.WriteLine($"  {sql.Database}: {sql.Objects.Count} objects, {sql.HealthIssues.Count} health issues");
        foreach (var i in sql.HealthIssues.Take(20))
            Console.WriteLine($"    [{i.Kind}] {i.Object}: {i.Message}");
    }

    return new Manifest(DateTime.UtcNow.ToString("u"), app, views, sql);
}

void Write(string path, Manifest m)
{
    File.WriteAllText(path, JsonSerializer.Serialize(m, jsonOut));
    Console.WriteLine($"Wrote {Path.GetFullPath(path)}");
}

static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

static Dictionary<string, string?> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith("--")) continue;
        var key = a[i][2..];
        string? val = (i + 1 < a.Length && !a[i + 1].StartsWith("--")) ? a[++i] : null;
        d[key] = val;
    }
    return d;
}

record InventorySettings
{
    public string Assembly { get; init; } = "";
    public string Views { get; init; } = "";
    public string LegacyAssembly { get; init; } = "";
    public string LegacyViews { get; init; } = "";
    public string SqlConnection { get; init; } = "";
    public bool CheckSqlHealth { get; init; } = true;
    public string OutputFolder { get; init; } = @"..\..\..\..\WebApp.IntegrationTests\manifests";
    public bool PauseAtEnd { get; init; } = true;
}
