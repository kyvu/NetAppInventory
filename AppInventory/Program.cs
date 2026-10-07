using System.Text.Json;
using System.Text.Json.Serialization;
using AppInventory;

// Usage:
//   AppInventory --assembly <path to web .dll in bin> [--views <Views folder>]
//                [--sql "<connection string>"] [--check-sql-health] [--out manifest.json]
//
// Examples (run on your dev box):
//   AppInventory --assembly C:\src\MyLegacyApp\bin\MyLegacyApp.dll --views C:\src\MyLegacyApp\Views --out manifest-net48.json
//   AppInventory --assembly C:\src\MyApp\bin\Debug\net10.0\MyApp.dll --views C:\src\MyApp\Views --out manifest-net10.json
//   AppInventory --sql "Server=localhost;Database=MyApp_Test;Integrated Security=True;TrustServerCertificate=True" --check-sql-health --out manifest-sql.json

var opts = ParseArgs(args);
if (opts.Count == 0 || opts.ContainsKey("help"))
{
    Console.WriteLine("""
        AppInventory – structure-only manifest for test generation (no data, no secrets)

          --assembly <dll>        Web app assembly (.NET 4.8 or .NET 10) in its bin folder
          --views <folder>        Views root to scan .cshtml files
          --sql "<connstr>"       SQL Server connection string (Windows auth recommended)
          --check-sql-health      Also run SELECT TOP 0 on every view to find broken bindings
          --out <file>            Output file (default manifest.json)
        """);
    return 1;
}

AppInfo? app = null;
ViewInfo? views = null;
SqlInfo? sql = null;

if (opts.TryGetValue("assembly", out var asmPath))
{
    Console.WriteLine($"Scanning controllers in {asmPath} ...");
    app = ControllerScanner.Scan(asmPath!);
    Console.WriteLine($"  {app.Framework}: {app.Controllers.Count} controllers, {app.Controllers.Sum(c => c.Actions.Count)} actions");
}

if (opts.TryGetValue("views", out var viewsPath))
{
    Console.WriteLine($"Scanning views in {viewsPath} ...");
    views = ViewScanner.Scan(viewsPath!);
    Console.WriteLine($"  {views.Files.Count} views, {views.Files.Sum(f => f.Warnings.Count)} warnings");
}

if (opts.TryGetValue("sql", out var connStr))
{
    Console.WriteLine("Scanning SQL schema ...");
    sql = SqlScanner.Scan(connStr!, opts.ContainsKey("check-sql-health"));
    Console.WriteLine($"  {sql.Database}: {sql.Objects.Count} objects, {sql.HealthIssues.Count} health issues");
    foreach (var i in sql.HealthIssues.Take(20))
        Console.WriteLine($"    [{i.Kind}] {i.Object}: {i.Message}");
}

var manifest = new Manifest(DateTime.UtcNow.ToString("u"), app, views, sql);
var outPath = opts.GetValueOrDefault("out") ?? "manifest.json";

File.WriteAllText(outPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
}));

Console.WriteLine($"Wrote {Path.GetFullPath(outPath)}");
return 0;

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
