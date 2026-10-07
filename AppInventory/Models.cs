namespace AppInventory;

// Everything in the manifest is STRUCTURE ONLY: names, types, routes.
// No row data, no connection strings, no PII ever goes in here.

public record Manifest(
    string GeneratedAtUtc,
    AppInfo? App,
    ViewInfo? Views,
    SqlInfo? Sql);

// ---------- App (controllers / actions) ----------

public record AppInfo(
    string AssemblyName,
    string Framework,               // "net48" or "netcore"
    List<ControllerInfo> Controllers);

public record ControllerInfo(
    string Name,                    // "Orders"
    string FullTypeName,
    string? Area,
    string? RoutePrefix,
    List<string> Authorize,         // e.g. ["Authorize", "Roles=Admin"]
    List<ActionInfo> Actions);

public record ActionInfo(
    string Name,
    string ActionName,              // [ActionName] override, else method name
    List<string> Verbs,             // GET / POST / ...
    string? Route,
    string ReturnType,              // ActionResult, PartialViewResult, JsonResult, Task<...>
    bool IsChildActionOnly,         // [ChildActionOnly] – rendered via Html.Action, not directly routable
    bool AllowAnonymous,
    List<string> Authorize,
    List<ParamInfo> Parameters);

public record ParamInfo(
    string Name,
    string Type,
    bool IsOptional,
    string? DefaultValue,
    string? BindingSource);         // FromBody / FromQuery / ... if declared

// ---------- Views (.cshtml static scan) ----------

public record ViewInfo(List<ViewFile> Files);

public record ViewFile(
    string Path,                    // relative to views root
    string? Model,
    List<string> Forms,             // "Controller/Action [POST]"
    List<string> ActionCalls,       // Html.Action / ActionLink / custom *Action* helpers / asp-action targets
    List<string> Partials,
    List<string> ElementIds,
    List<string> InputNames,
    List<string> DataModalLinks,
    List<string> InlineHandlers,    // onclick="doSomething(this)" etc – fragile spots worth UI tests
    List<string> Warnings);         // duplicate ids, ids inside @foreach, etc.

// ---------- SQL ----------

public record SqlInfo(
    string Database,
    List<SqlObject> Objects,
    List<SqlHealthIssue> HealthIssues);

public record SqlObject(
    string Schema,
    string Name,
    string Type,                    // TABLE / VIEW / PROC / FUNCTION
    List<SqlColumn> Columns,        // tables & views
    List<SqlParam> Parameters,      // procs & functions
    List<string> References);       // objects this one depends on

public record SqlColumn(string Name, string Type, bool Nullable, bool IsIdentity, bool IsPrimaryKey);

public record SqlParam(string Name, string Type, bool IsOutput, bool HasDefault);

public record SqlHealthIssue(string Object, string Kind, string Message);
