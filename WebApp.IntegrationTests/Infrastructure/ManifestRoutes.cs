using System.Text.Json;

namespace WebApp.IntegrationTests.Infrastructure;

// Minimal shapes of the AppInventory manifest – only what the tests need.
public record AppManifest(AppSection? App);
public record AppSection(List<Ctrl> Controllers);
public record Ctrl(string Name, string? Area, List<Act> Actions);
public record Act(string Name, string ActionName, List<string> Verbs, string? Route,
                  string ReturnType, bool IsChildActionOnly, List<Prm> Parameters);
public record Prm(string Name, string Type, bool IsOptional, string? BindingSource);

public record SqlManifest(SqlSection? Sql);
public record SqlSection(string Database, List<SqlObj> Objects);
public record SqlObj(string Schema, string Name, string Type);

public record RouteCase(string Key, string Url);

/// <summary>
/// Turns the manifest into a list of GET URLs that can be smoke-tested.
/// </summary>
public static class ManifestRoutes
{
    static readonly HashSet<string> SimpleTypes = new(StringComparer.OrdinalIgnoreCase)
    { "Int32", "Int64", "String", "Boolean", "Guid", "DateTime", "Decimal", "Nullable<Int32>", "Nullable<Int64>", "Nullable<Boolean>", "Nullable<DateTime>" };

    public static readonly List<string> Skipped = new();

    public static IEnumerable<RouteCase> GetRoutes()
    {
        var cfg = TestConfig.Current;
        var manifestPath = TestConfig.PathInOutput(cfg.AppManifest);
        if (!File.Exists(manifestPath))
        {
            Skipped.Add($"Manifest not found: {manifestPath}");
            yield break;
        }

        var manifest = JsonSerializer.Deserialize<AppManifest>(File.ReadAllText(manifestPath), TestConfig.Json);
        var samples = LoadSamples();

        foreach (var c in manifest?.App?.Controllers ?? new())
        foreach (var a in c.Actions)
        {
            var key = $"{c.Name}/{a.ActionName}";

            if (!a.Verbs.Contains("GET")) continue;                 // POSTs need form data – covered by UI tests
            if (a.IsChildActionOnly) { Skipped.Add($"{key} (child action only)"); continue; }
            if (a.Route?.Contains('{') == true && !samples.ContainsKey(key))
            { Skipped.Add($"{key} (route template '{a.Route}' – add to route-samples.json)"); continue; }

            var required = a.Parameters.Where(p => !p.IsOptional).ToList();
            var complex = required.Where(p => !SimpleTypes.Contains(p.Type)).ToList();

            string query;
            if (samples.TryGetValue(key, out var sample)) query = sample;
            else if (required.Count == 0) query = "";
            else
            {
                Skipped.Add($"{key} (needs: {string.Join(", ", required.Select(p => $"{p.Type} {p.Name}"))})"
                            + (complex.Count > 0 ? " [complex type]" : ""));
                continue;
            }

            var path = string.IsNullOrEmpty(c.Area) ? $"/{c.Name}/{a.ActionName}" : $"/{c.Area}/{c.Name}/{a.ActionName}";
            yield return new RouteCase(key, string.IsNullOrEmpty(query) ? path : $"{path}?{query}");
        }
    }

    public static IEnumerable<string> GetViews()
    {
        var path = TestConfig.PathInOutput(TestConfig.Current.SqlManifest);
        if (!File.Exists(path)) yield break;
        var m = JsonSerializer.Deserialize<SqlManifest>(File.ReadAllText(path), TestConfig.Json);
        foreach (var o in m?.Sql?.Objects ?? new())
            if (o.Type == "VIEW") yield return $"{o.Schema}.{o.Name}";
    }

    static Dictionary<string, string> LoadSamples()
    {
        var path = TestConfig.PathInOutput("route-samples.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), TestConfig.Json)
               ?? new();
    }
}
