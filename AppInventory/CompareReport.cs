using System.Text;

namespace AppInventory;

/// <summary>
/// Compares a legacy manifest (e.g. .NET 4.8) with a new one (e.g. .NET 10) and writes a
/// readable report of what's missing, added, or changed. Structure only – no data.
/// </summary>
public static class CompareReport
{
    public static string Build(Manifest legacy, Manifest current)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration compare");
        sb.AppendLine();
        sb.AppendLine($"Legacy:  {legacy.App?.AssemblyName} ({legacy.App?.Framework})");
        sb.AppendLine($"Current: {current.App?.AssemblyName} ({current.App?.Framework})");
        sb.AppendLine();

        CompareControllers(sb, legacy.App, current.App);
        CompareViews(sb, legacy.Views, current.Views);
        return sb.ToString();
    }

    static void CompareControllers(StringBuilder sb, AppInfo? oldApp, AppInfo? newApp)
    {
        if (oldApp == null || newApp == null) return;

        var oldCtrls = oldApp.Controllers.ToDictionary(c => Key(c.Area, c.Name), StringComparer.OrdinalIgnoreCase);
        var newCtrls = newApp.Controllers.ToDictionary(c => Key(c.Area, c.Name), StringComparer.OrdinalIgnoreCase);

        var missingCtrls = oldCtrls.Keys.Except(newCtrls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var addedCtrls = newCtrls.Keys.Except(oldCtrls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

        var missingActions = new List<string>();
        var addedActions = new List<string>();
        var changedActions = new List<string>();

        foreach (var key in oldCtrls.Keys.Intersect(newCtrls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
        {
            var oldActs = Index(oldCtrls[key].Actions);
            var newActs = Index(newCtrls[key].Actions);

            foreach (var a in oldActs.Keys.Except(newActs.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                missingActions.Add($"{key}/{a}  ({Sig(oldActs[a])})");
            foreach (var a in newActs.Keys.Except(oldActs.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                addedActions.Add($"{key}/{a}  ({Sig(newActs[a])})");

            foreach (var a in oldActs.Keys.Intersect(newActs.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                var o = oldActs[a]; var n = newActs[a];
                var diffs = new List<string>();
                if (!ParamNames(o).SetEquals(ParamNames(n)))
                    diffs.Add($"params: ({Sig(o)}) -> ({Sig(n)})");
                var oAuth = string.Join(";", o.Authorize); var nAuth = string.Join(";", n.Authorize);
                if (!string.Equals(oAuth, nAuth, StringComparison.OrdinalIgnoreCase))
                    diffs.Add($"auth: [{oAuth}] -> [{nAuth}]");
                if (diffs.Count > 0) changedActions.Add($"{key}/{a}: {string.Join("; ", diffs)}");
            }
        }

        // Controller-level [Authorize] changes
        foreach (var key in oldCtrls.Keys.Intersect(newCtrls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
        {
            var oAuth = string.Join(";", oldCtrls[key].Authorize); var nAuth = string.Join(";", newCtrls[key].Authorize);
            if (!string.Equals(oAuth, nAuth, StringComparison.OrdinalIgnoreCase))
                changedActions.Insert(0, $"{key} (controller): auth [{oAuth}] -> [{nAuth}]");
        }

        int oldCount = oldApp.Controllers.Sum(c => c.Actions.Count), newCount = newApp.Controllers.Sum(c => c.Actions.Count);
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Controllers: {oldCtrls.Count} legacy → {newCtrls.Count} current");
        sb.AppendLine($"- Actions: {oldCount} legacy → {newCount} current");
        sb.AppendLine($"- Missing controllers: {missingCtrls.Count}, missing actions: {missingActions.Count}");
        sb.AppendLine($"- Changed actions (params/auth): {changedActions.Count}");
        sb.AppendLine($"- New in current: {addedCtrls.Count} controllers, {addedActions.Count} actions");
        sb.AppendLine();

        Section(sb, "Missing controllers (in legacy, not in current)", missingCtrls.Select(k =>
            $"{k}  ({oldCtrls[k].Actions.Count} actions)"));
        Section(sb, "Missing actions (in legacy, not in current)", missingActions);
        Section(sb, "Changed actions (review: parameters or authorization differ)", changedActions);
        Section(sb, "New controllers (only in current)", addedCtrls);
        Section(sb, "New actions (only in current)", addedActions);
    }

    static void CompareViews(StringBuilder sb, ViewInfo? oldViews, ViewInfo? newViews)
    {
        if (oldViews == null || newViews == null) return;

        var oldSet = oldViews.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newSet = newViews.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Ignore files that are expected to differ between frameworks
        static bool Ignore(string p) =>
            p.EndsWith("web.config", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase);

        var missing = oldSet.Where(p => !newSet.Contains(p) && !Ignore(p)).OrderBy(p => p).ToList();
        var added = newSet.Where(p => !oldSet.Contains(p) && !Ignore(p)).OrderBy(p => p).ToList();

        sb.AppendLine($"## Views: {oldSet.Count} legacy → {newSet.Count} current ({missing.Count} missing)");
        sb.AppendLine();
        Section(sb, "Missing views (in legacy, not in current)", missing);
        Section(sb, "New views (only in current)", added);
    }

    // ---------- helpers ----------

    static string Key(string? area, string name) => string.IsNullOrEmpty(area) ? name : $"{area}/{name}";

    /// Action key = ActionName + verb, so GET Edit and POST Edit are tracked separately.
    static Dictionary<string, ActionInfo> Index(IEnumerable<ActionInfo> actions)
    {
        var d = new Dictionary<string, ActionInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in actions)
        {
            var k = $"{a.ActionName} [{string.Join("|", a.Verbs.OrderBy(v => v))}]";
            d.TryAdd(k, a); // overloads with same name+verb: first wins
        }
        return d;
    }

    static HashSet<string> ParamNames(ActionInfo a) =>
        a.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static string Sig(ActionInfo a) =>
        string.Join(", ", a.Parameters.Select(p => $"{p.Type} {p.Name}{(p.IsOptional ? "?" : "")}"));

    static void Section(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items.ToList();
        sb.AppendLine($"### {title} ({list.Count})");
        if (list.Count == 0) sb.AppendLine("_none_");
        foreach (var i in list) sb.AppendLine($"- {i}");
        sb.AppendLine();
    }
}
