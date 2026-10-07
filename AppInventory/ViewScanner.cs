using System.Text.RegularExpressions;

namespace AppInventory;

/// <summary>
/// Static, regex-based scan of .cshtml files. Not a Razor parser – it's meant to give
/// enough structure (forms, links, ids, partials) to generate page objects and UI tests.
/// </summary>
public static class ViewScanner
{
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    static readonly Regex ModelRx       = new(@"^\s*@model\s+(.+)$", O | RegexOptions.Multiline);
    static readonly Regex BeginFormRx   = new(@"Html\.BeginForm\(\s*""([^""]+)""\s*,\s*""([^""]+)""(?:\s*,\s*FormMethod\.(\w+))?", O);
    static readonly Regex TagFormRx     = new(@"<form[^>]*\basp-action=""([^""]+)""[^>]*?(?:asp-controller=""([^""]+)"")?[^>]*>", O);
    static readonly Regex HtmlActionRx  = new(@"(?:Html\.\w*Action\w*|Html\.BeginRouteForm|Url\.Action)\(([^)]*)\)", O);
    static readonly Regex QuotedRx      = new(@"""([^""]*)""", O);
    static readonly Regex AspActionRx   = new(@"asp-action=""([^""]+)""(?:[^>]*asp-controller=""([^""]+)"")?", O);
    static readonly Regex PartialRx     = new(@"(?:Html\.(?:Partial|RenderPartial|PartialAsync|RenderPartialAsync)\(\s*""([^""]+)""|<partial\s+name=""([^""]+)"")", O);
    static readonly Regex IdRx          = new(@"\bid\s*=\s*""([^""@]+)""|@id\s*=\s*""([^""]+)""", O);
    static readonly Regex NameRx        = new(@"<(?:input|select|textarea)[^>]*\bname\s*=\s*""([^""]+)""", O);
    static readonly Regex ForHelperRx   = new(@"Html\.(?:EditorFor|TextBoxFor|DropDownListFor|HiddenFor|TextAreaFor|CheckBoxFor|PasswordFor)\(\s*\w+\s*=>\s*\w+\.([\w.]+)", O);
    static readonly Regex DataModalRx   = new(@"data[-_]modal[^>]*?(?:href=""([^""]+)""|)", O);
    static readonly Regex InlineHandler = new(@"\b(on(?:click|mousedown|mouseup|change|submit|keyup))\s*=\s*""([^""]+)""", O);
    static readonly Regex ForeachRx     = new(@"@foreach\s*\(", O);

    public static ViewInfo Scan(string viewsRoot)
    {
        viewsRoot = Path.GetFullPath(viewsRoot);
        var files = Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .Select(f => ScanFile(viewsRoot, f))
            .ToList();
        return new ViewInfo(files);
    }

    static ViewFile ScanFile(string root, string path)
    {
        var text = File.ReadAllText(path);
        var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        var warnings = new List<string>();

        var forms = new List<string>();
        foreach (Match m in BeginFormRx.Matches(text))
            forms.Add($"{m.Groups[2].Value}/{m.Groups[1].Value} [{(m.Groups[3].Success ? m.Groups[3].Value.ToUpper() : "POST")}]");
        foreach (Match m in TagFormRx.Matches(text))
            forms.Add($"{(m.Groups[2].Success ? m.Groups[2].Value : "?")}/{m.Groups[1].Value} [POST]");

        var actions = new List<string>();
        foreach (Match m in HtmlActionRx.Matches(text))
        {
            var q = QuotedRx.Matches(m.Groups[1].Value).Select(x => x.Groups[1].Value).ToList();
            if (q.Count > 0) actions.Add(string.Join(" | ", q));
        }
        foreach (Match m in AspActionRx.Matches(text))
            actions.Add($"{(m.Groups[2].Success ? m.Groups[2].Value : "?")}/{m.Groups[1].Value}");

        var partials = PartialRx.Matches(text)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

        var ids = IdRx.Matches(text)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        foreach (var dup in ids.GroupBy(i => i).Where(g => g.Count() > 1))
            warnings.Add($"duplicate id '{dup.Key}' ({dup.Count()}x)");

        // Static ids inside a @foreach = duplicate ids at runtime (breaks getElementById and per-row JS)
        foreach (Match fe in ForeachRx.Matches(text))
        {
            var block = ExtractBlock(text, fe.Index);
            foreach (Match idm in IdRx.Matches(block))
            {
                var id = idm.Groups[1].Success ? idm.Groups[1].Value : idm.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(id))
                    warnings.Add($"static id '{id}' inside @foreach – duplicated per row");
            }
        }

        var names = NameRx.Matches(text).Select(m => m.Groups[1].Value)
            .Concat(ForHelperRx.Matches(text).Select(m => m.Groups[1].Value))
            .ToList();

        var modals = DataModalRx.Matches(text)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : "(helper)")
            .ToList();

        var handlers = InlineHandler.Matches(text)
            .Select(m => $"{m.Groups[1].Value}: {m.Groups[2].Value}").ToList();

        return new ViewFile(
            Path: rel,
            Model: ModelRx.Match(text) is { Success: true } mm ? mm.Groups[1].Value.Trim() : null,
            Forms: forms.Distinct().ToList(),
            ActionCalls: actions.Distinct().ToList(),
            Partials: partials.Distinct().ToList(),
            ElementIds: ids.Distinct().ToList(),
            InputNames: names.Distinct().ToList(),
            DataModalLinks: modals.Distinct().ToList(),
            InlineHandlers: handlers.Distinct().ToList(),
            Warnings: warnings.Distinct().ToList());
    }

    /// Returns the text of the { ... } block that starts after index (brace matching).
    static string ExtractBlock(string text, int start)
    {
        int open = text.IndexOf('{', start);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return text[open..];
    }
}
