using Microsoft.Data.SqlClient;

namespace AppInventory;

/// <summary>
/// Reads schema metadata only (sys.* catalog views). Never selects table data.
/// Optional health check runs "SELECT TOP 0 *" on every view – read-only, returns no rows,
/// but fails on broken bindings (e.g. "Invalid column name ..." after a column rename).
/// </summary>
public static class SqlScanner
{
    public static SqlInfo Scan(string connectionString, bool checkHealth)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();

        var objects = LoadObjects(conn);
        LoadColumns(conn, objects);
        LoadParameters(conn, objects);
        LoadReferences(conn, objects);

        var issues = new List<SqlHealthIssue>();
        issues.AddRange(UnresolvedReferences(conn));
        if (checkHealth) issues.AddRange(CheckViews(conn, objects.Values));

        return new SqlInfo(conn.Database, objects.Values.OrderBy(o => o.Type).ThenBy(o => o.Schema).ThenBy(o => o.Name).ToList(), issues);
    }

    static Dictionary<int, SqlObject> LoadObjects(SqlConnection conn)
    {
        const string sql = @"
SELECT o.object_id, s.name, o.name,
       CASE o.type WHEN 'U' THEN 'TABLE' WHEN 'V' THEN 'VIEW' WHEN 'P' THEN 'PROC'
                   ELSE 'FUNCTION' END
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.type IN ('U','V','P','FN','IF','TF') AND o.is_ms_shipped = 0;";

        var dict = new Dictionary<int, SqlObject>();
        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dict[r.GetInt32(0)] = new SqlObject(r.GetString(1), r.GetString(2), r.GetString(3), new(), new(), new());
        return dict;
    }

    static void LoadColumns(SqlConnection conn, Dictionary<int, SqlObject> objs)
    {
        const string sql = @"
SELECT c.object_id, c.name,
       t.name + CASE WHEN t.name IN ('varchar','nvarchar','char','nchar','varbinary')
                     THEN '(' + CASE WHEN c.max_length = -1 THEN 'max'
                                     WHEN t.name LIKE 'n%' THEN CAST(c.max_length/2 AS varchar(10))
                                     ELSE CAST(c.max_length AS varchar(10)) END + ')'
                     WHEN t.name IN ('decimal','numeric')
                     THEN '(' + CAST(c.precision AS varchar(5)) + ',' + CAST(c.scale AS varchar(5)) + ')'
                     ELSE '' END,
       c.is_nullable, c.is_identity,
       CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic
                         JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                         WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id)
            THEN 1 ELSE 0 END
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
ORDER BY c.object_id, c.column_id;";

        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (objs.TryGetValue(r.GetInt32(0), out var o))
                o.Columns.Add(new SqlColumn(r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4), r.GetInt32(5) == 1));
    }

    static void LoadParameters(SqlConnection conn, Dictionary<int, SqlObject> objs)
    {
        const string sql = @"
SELECT p.object_id, p.name, t.name, p.is_output, p.has_default_value
FROM sys.parameters p
JOIN sys.types t ON t.user_type_id = p.user_type_id
WHERE p.name <> ''
ORDER BY p.object_id, p.parameter_id;";

        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (objs.TryGetValue(r.GetInt32(0), out var o))
                o.Parameters.Add(new SqlParam(r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4)));
    }

    static void LoadReferences(SqlConnection conn, Dictionary<int, SqlObject> objs)
    {
        const string sql = @"
SELECT d.referencing_id,
       ISNULL(d.referenced_schema_name + '.', '') + d.referenced_entity_name
FROM sys.sql_expression_dependencies d
WHERE d.referenced_id IS NOT NULL;";

        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (objs.TryGetValue(r.GetInt32(0), out var o) && !o.References.Contains(r.GetString(1)))
                o.References.Add(r.GetString(1));
    }

    /// References SQL Server could not resolve (dropped/renamed objects). May include false
    /// positives for temp tables or cross-database references – treat as "review".
    static IEnumerable<SqlHealthIssue> UnresolvedReferences(SqlConnection conn)
    {
        const string sql = @"
SELECT s.name + '.' + o.name, d.referenced_entity_name
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE d.referenced_id IS NULL
  AND d.is_ambiguous = 0
  AND d.referenced_database_name IS NULL
  AND d.referenced_entity_name NOT LIKE '#%';";

        var list = new List<SqlHealthIssue>();
        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SqlHealthIssue(r.GetString(0), "UnresolvedReference", $"references '{r.GetString(1)}' which does not exist (review)"));
        return list;
    }

    static IEnumerable<SqlHealthIssue> CheckViews(SqlConnection conn, IEnumerable<SqlObject> objs)
    {
        var list = new List<SqlHealthIssue>();
        foreach (var v in objs.Where(o => o.Type == "VIEW"))
        {
            try
            {
                using var cmd = new SqlCommand($"SELECT TOP 0 * FROM [{v.Schema}].[{v.Name}];", conn) { CommandTimeout = 30 };
                cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                list.Add(new SqlHealthIssue($"{v.Schema}.{v.Name}", "BrokenView", ex.Message.Split('\n')[0]));
            }
        }
        return list;
    }
}
