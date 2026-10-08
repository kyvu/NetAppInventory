using System.Reflection;
using System.Runtime.InteropServices;

namespace AppInventory;

/// <summary>
/// Reads controllers/actions from a compiled web assembly WITHOUT executing it.
/// Uses MetadataLoadContext, so it works on both the .NET 4.8 DLL and the .NET 10 DLL
/// and doesn't need the app's config, DB, or IIS.
/// </summary>
public static class ControllerScanner
{
    // Framework base types we stop at. Comparing by name because MetadataLoadContext
    // types can't be compared with typeof().
    static readonly HashSet<string> FrameworkBases = new()
    {
        "System.Web.Mvc.Controller",
        "System.Web.Mvc.ControllerBase",
        "System.Web.Mvc.AsyncController",
        "System.Web.Http.ApiController",
        "Microsoft.AspNetCore.Mvc.Controller",
        "Microsoft.AspNetCore.Mvc.ControllerBase",
    };

    static readonly Dictionary<string, string> VerbAttributes = new()
    {
        ["HttpGetAttribute"] = "GET",
        ["HttpPostAttribute"] = "POST",
        ["HttpPutAttribute"] = "PUT",
        ["HttpDeleteAttribute"] = "DELETE",
        ["HttpPatchAttribute"] = "PATCH",
    };

    public static AppInfo Scan(string assemblyPath)
    {
        assemblyPath = Path.GetFullPath(assemblyPath);
        var binDir = Path.GetDirectoryName(assemblyPath)!;
        bool isNet48 = File.Exists(Path.Combine(binDir, "System.Web.Mvc.dll"));

        var paths = new List<string>(Directory.GetFiles(binDir, "*.dll"));
        string coreAssembly;

        if (isNet48)
        {
            // .NET Framework 4.8: resolve framework assemblies (System.Web, mscorlib...) from the GAC folder.
            var fx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                  @"Microsoft.NET\Framework64\v4.0.30319");
            if (!Directory.Exists(fx))
                throw new DirectoryNotFoundException($".NET Framework folder not found: {fx}. Run this on the Windows dev box.");
            paths.AddRange(Directory.GetFiles(fx, "*.dll"));
            var wpf = Path.Combine(fx, "WPF");
            if (Directory.Exists(wpf)) paths.AddRange(Directory.GetFiles(wpf, "*.dll"));
            coreAssembly = "mscorlib";
        }
        else
        {
            // .NET 10: core runtime + ASP.NET Core shared framework.
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
            var aspNetDir = FindAspNetSharedFramework(runtimeDir);
            if (aspNetDir != null) paths.AddRange(Directory.GetFiles(aspNetDir, "*.dll"));
            coreAssembly = "System.Private.CoreLib";
        }

        // Dedupe by file name, bin folder wins (it's added first).
        var unique = paths
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(unique), coreAssembly);
        var asm = mlc.LoadFromAssemblyPath(assemblyPath);

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
            Console.WriteLine($"  [warn] {ex.LoaderExceptions.Length} types could not be loaded (missing dependency) – skipped.");
        }

        var controllers = types
            .Where(IsController)
            .OrderBy(t => t.Name)
            .Select(BuildController)
            .ToList();

        return new AppInfo(asm.GetName().Name!, isNet48 ? "net48" : "netcore", controllers);
    }

    static string? FindAspNetSharedFramework(string runtimeDir)
    {
        // runtimeDir = .../shared/Microsoft.NETCore.App/10.x.y/
        var shared = Directory.GetParent(runtimeDir.TrimEnd(Path.DirectorySeparatorChar))?.Parent;
        var aspRoot = shared == null ? null : Path.Combine(shared.FullName, "Microsoft.AspNetCore.App");
        if (aspRoot == null || !Directory.Exists(aspRoot)) return null;
        return Directory.GetDirectories(aspRoot).OrderByDescending(d => d).FirstOrDefault();
    }

    static bool IsController(Type t)
    {
        try
        {
            if (!t.IsClass || t.IsAbstract || !t.IsPublic) return false;
            if (HasAttr(t, "NonControllerAttribute")) return false;
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.FullName != null && FrameworkBases.Contains(b.FullName)) return true;
        }
        catch { /* base type in a missing assembly */ }
        return false;
    }

    static ControllerInfo BuildController(Type t)
    {
        var name = t.Name.EndsWith("Controller") ? t.Name[..^"Controller".Length] : t.Name;
        return new ControllerInfo(
            Name: name,
            FullTypeName: t.FullName ?? t.Name,
            Area: AttrArg(t, "AreaAttribute") ?? AttrArg(t, "RouteAreaAttribute"),
            RoutePrefix: AttrArg(t, "RoutePrefixAttribute") ?? AttrArg(t, "RouteAttribute"),
            Authorize: AuthInfo(t),
            Actions: GetActionMethods(t).Select(BuildAction).OrderBy(a => a.Name).ToList());
    }

    /// Public instance methods declared on the controller or on the app's own base controllers
    /// (stops at the framework Controller type so we don't list View(), Json(), etc.).
    static IEnumerable<MethodInfo> GetActionMethods(Type t)
    {
        var seen = new HashSet<string>();
        for (var cur = t; cur != null && !(cur.FullName != null && FrameworkBases.Contains(cur.FullName)); cur = cur.BaseType)
        {
            foreach (var m in cur.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName || m.IsGenericMethodDefinition) continue;
                if (HasAttr(m, "NonActionAttribute")) continue;
                if (m.Name is "Dispose" or "ToString" or "GetHashCode" or "Equals") continue;
                var sig = m.Name + "(" + string.Join(",", m.GetParameters().Select(p => SafeTypeName(p.ParameterType))) + ")";
                if (seen.Add(sig)) yield return m;   // overridden methods appear once
            }
        }
    }

    static ActionInfo BuildAction(MethodInfo m)
    {
        var verbs = m.GetCustomAttributesData()
            .Select(a => a.AttributeType.Name)
            .Where(VerbAttributes.ContainsKey)
            .Select(n => VerbAttributes[n])
            .ToList();

        // [AcceptVerbs(HttpVerbs.Post)] style
        var accept = m.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "AcceptVerbsAttribute");
        if (accept != null) verbs.AddRange(DecodeAcceptVerbs(accept));

        if (verbs.Count == 0) verbs.Add("GET"); // MVC default when no verb attribute

        return new ActionInfo(
            Name: m.Name,
            ActionName: AttrArg(m, "ActionNameAttribute") ?? m.Name,
            Verbs: verbs.Distinct().ToList(),
            Route: AttrArg(m, "RouteAttribute") ?? VerbRoute(m),
            ReturnType: SafeTypeName(m.ReturnType),
            IsChildActionOnly: HasAttr(m, "ChildActionOnlyAttribute"),
            AllowAnonymous: HasAttr(m, "AllowAnonymousAttribute"),
            Authorize: AuthInfo(m),
            Parameters: m.GetParameters().Select(BuildParam).ToList());
    }

    /// [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Post)] (enum flags) or [AcceptVerbs("GET", "POST")] (string array).
    static IEnumerable<string> DecodeAcceptVerbs(CustomAttributeData accept)
    {
        var flagNames = new (int Bit, string Verb)[] { (1, "GET"), (2, "POST"), (4, "PUT"), (8, "DELETE"), (16, "HEAD"), (32, "PATCH"), (64, "OPTIONS") };
        foreach (var arg in accept.ConstructorArguments)
        {
            if (arg.Value is IEnumerable<CustomAttributeTypedArgument> items)          // params string[]
            {
                foreach (var i in items)
                    if (i.Value != null) yield return i.Value.ToString()!.ToUpperInvariant();
            }
            else if (arg.Value is int flags)                                            // HttpVerbs enum
            {
                foreach (var (bit, verb) in flagNames)
                    if ((flags & bit) != 0) yield return verb;
            }
            else if (arg.Value != null)
                yield return arg.Value.ToString()!.ToUpperInvariant();
        }
    }

    static ParamInfo BuildParam(ParameterInfo p)
    {
        string? binding = p.GetCustomAttributesData()
            .Select(a => a.AttributeType.Name)
            .FirstOrDefault(n => n.StartsWith("From") || n == "BindAttribute");

        string? def = null;
        if (p.HasDefaultValue)
        {
            try { def = p.RawDefaultValue?.ToString() ?? "null"; } catch { def = "?"; }
        }

        return new ParamInfo(p.Name ?? "", SafeTypeName(p.ParameterType), p.IsOptional, def, binding);
    }

    // ---------- helpers ----------

    static string? VerbRoute(MethodInfo m) =>
        m.GetCustomAttributesData()
         .Where(a => VerbAttributes.ContainsKey(a.AttributeType.Name) && a.ConstructorArguments.Count > 0)
         .Select(a => a.ConstructorArguments[0].Value?.ToString())
         .FirstOrDefault();

    static List<string> AuthInfo(MemberInfo mi)
    {
        var list = new List<string>();
        foreach (var a in mi.GetCustomAttributesData())
        {
            var n = a.AttributeType.Name;
            if (!n.Contains("Authorize")) continue;   // catches custom *AuthorizeAttribute types too
            var parts = new List<string> { n.Replace("Attribute", "") };
            parts.AddRange(a.ConstructorArguments.Select(c => c.Value?.ToString() ?? ""));
            parts.AddRange(a.NamedArguments.Select(na => $"{na.MemberName}={na.TypedValue.Value}"));
            list.Add(string.Join(" ", parts.Where(s => s.Length > 0)));
        }
        return list;
    }

    static bool HasAttr(MemberInfo mi, string attrName)
    {
        try { return mi.GetCustomAttributesData().Any(a => a.AttributeType.Name == attrName); }
        catch { return false; }
    }

    static string? AttrArg(MemberInfo mi, string attrName)
    {
        try
        {
            var a = mi.GetCustomAttributesData().FirstOrDefault(x => x.AttributeType.Name == attrName);
            return a?.ConstructorArguments.FirstOrDefault().Value?.ToString();
        }
        catch { return null; }
    }

    static string SafeTypeName(Type t)
    {
        try
        {
            if (!t.IsGenericType) return t.Name;
            var baseName = t.Name.Split('`')[0];
            return $"{baseName}<{string.Join(",", t.GetGenericArguments().Select(SafeTypeName))}>";
        }
        catch { return "?"; }
    }
}
