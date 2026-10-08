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

        var missingCtrls = oldCtrls.Keys.Except(newCtrls.Keys, StringComparer.OrdinalIgnoreCase).Where(k => oldCtrls[k].Actions.Count > 0).OrderBy(k => k).ToList();
        var addedCtrls = newCtrls.Keys.Except(oldCtrls.Keys, StringComparer.OrdinalIgnoreCase).Where(k => newCtrls[k].Actions.Count > 0).OrderBy(k => k).ToList();

        var missingActions = new List<string>();
        var addedActions = new List<string>();
        var authChanges = new List<string>();
        var paramChanges = new List<string>();

        // Controller-level auth changes
        foreach (var key in oldCtrls.Keys.Intersect(newCtrls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
        {
            var o = NormAuth(oldCtrls[key].Authorize); var n = NormAuth(newCtrls[key].Authorize);
            if (o != n) authChanges.Add($"{key} (controller): {AuthDiff(o, n)}");
        }

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

                var oAuth = NormAuth(o.Authorize) + (o.AllowAnonymous ? " +AllowAnonymous" : "");
                var nAuth = NormAuth(n.Authorize) + (n.AllowAnonymous ? " +AllowAnonymous" : "");
                if (oAuth != nAuth) authChanges.Add($"{key}/{a}: {AuthDiff(oAuth, nAuth)}");

                if (!ParamNames(o).SetEquals(ParamNames(n)))
                    paramChanges.Add($"{key}/{a}: ({Sig(o)}) -> ({Sig(n)})");
            }
        }

        int oldCount = oldApp.Controllers.Sum(c => c.Actions.Count), newCount = newApp.Controllers.Sum(c => c.Actions.Count);
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Controllers: {oldCtrls.Count} legacy → {newCtrls.Count} current");
        sb.AppendLine($"- Actions: {oldCount} legacy → {newCount} current");
        sb.AppendLine($"- Missing controllers: {missingCtrls.Count}, missing actions: {missingActions.Count}");
        sb.AppendLine($"- Authorization changes: {authChanges.Count}  <-- review these first");
        sb.AppendLine($"- Parameter changes: {paramChanges.Count}");
        sb.AppendLine($"- New in current: {addedCtrls.Count} controllers, {addedActions.Count} actions");
        sb.AppendLine();

        Section(sb, "Authorization changes (roles/users/policy differ – attribute name and role casing ignored)", authChanges);
        Section(sb, "Missing controllers (in legacy, not in current)", missingCtrls.Select(k =>
            $"{k}  ({oldCtrls[k].Actions.Count} actions)"));
        Section(sb, "Missing actions (in legacy, not in current)", missingActions);
        Section(sb, "Parameter changes (names differ)", paramChanges);
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


    /// Normalizes [CustomAuthorize Roles=SystemAdmin, ITSystemAdmin] and [Authorize Roles=ITSYSTEMADMIN,SYSTEMADMIN]
    /// to the same string: attribute name ignored, keys/values upper-cased, role/user lists sorted.
    static string NormAuth(IEnumerable<string> entries)
    {
        var parts = new List<string>();
        foreach (var e in entries)
        {
            var body = e.Contains(' ') ? e[(e.IndexOf(' ') + 1)..] : "";   // drop attribute name
            var pairs = System.Text.RegularExpressions.Regex.Matches(body, @"(\w+)=(.*?)(?=\s+\w+=|$)");
            if (pairs.Count == 0)
            {
                parts.Add(string.IsNullOrWhiteSpace(body) ? "AUTHENTICATED" : body.Trim().ToUpperInvariant());
                continue;
            }
            foreach (System.Text.RegularExpressions.Match m in pairs)
            {
                var k = m.Groups[1].Value.ToUpperInvariant();
                var vals = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                            .Select(v => v.ToUpperInvariant()).OrderBy(v => v);
                parts.Add($"{k}={string.Join(",", vals)}");
            }
        }
        return string.Join("; ", parts.Distinct().OrderBy(p => p));
    }

    static string Show(string normalized) => normalized.Length == 0 ? "none" : normalized;

    /// Turns two normalized auth strings into "removed/added" items with a plain-English impact tag.
    static string AuthDiff(string oldAuth, string newAuth)
    {
        var o = Tokens(oldAuth); var n = Tokens(newAuth);
        var removed = o.Except(n).OrderBy(x => x).ToList();
        var added = n.Except(o).OrderBy(x => x).ToList();

        bool rolesAdded = added.Any(t => t.StartsWith("ROLES:"));
        bool rolesRemoved = removed.Any(t => t.StartsWith("ROLES:"));
        bool usersRemoved = removed.Any(t => t.StartsWith("USERS:"));
        bool anonAdded = added.Contains("ALLOWANONYMOUS");
        bool wasOpen = o.Count == 0 || (o.Count == 1 && o.Contains("AUTHENTICATED"));
        bool nowOpen = n.Count == 0 || (n.Count == 1 && n.Contains("AUTHENTICATED"));

        string impact =
            anonAdded || (nowOpen && !wasOpen) ? "⚠ MORE ACCESS (restriction removed)" :
            usersRemoved && !added.Any(t => t.StartsWith("USERS:") || t.StartsWith("ROLES:")) ? "⚠ CHECK: named-user list replaced" :
            rolesAdded && !rolesRemoved ? "⚠ MORE ACCESS (roles added)" :
            wasOpen && !nowOpen ? "tighter (was open)" :
            rolesRemoved && !rolesAdded ? "less access (roles removed – users may get 403)" :
            "changed";

        var parts = new List<string>();
        if (removed.Count > 0) parts.Add("removed " + string.Join(", ", removed.Select(Pretty)));
        if (added.Count > 0) parts.Add("added " + string.Join(", ", added.Select(Pretty)));
        return $"{impact} — {string.Join("; ", parts)}";
    }

    static HashSet<string> Tokens(string normalized)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in normalized.Replace("+AllowAnonymous", ";ALLOWANONYMOUS", StringComparison.OrdinalIgnoreCase).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = raw.IndexOf('=');
            if (eq < 0) { set.Add(raw.ToUpperInvariant()); continue; }
            var key = raw[..eq];
            foreach (var v in raw[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add($"{key}:{v}");
        }
        return set;
    }

    static string Pretty(string token) => token.Replace(":", " ");

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
