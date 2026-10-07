using WebApp.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace WebApp.IntegrationTests.Sql;

/// <summary>
/// Read-only schema checks. SELECT TOP 0 returns no rows but fails if a view's
/// definition is broken (e.g. "Invalid column name ..." after a column rename).
/// </summary>
[TestFixture, Category("Sql")]
public class SqlHealthTests
{
    static IEnumerable<TestCaseData> Views() =>
        ManifestRoutes.GetViews().Select(v => new TestCaseData(v).SetName($"View binds: {v}"));

    [OneTimeSetUp]
    public void RequireConnection()
    {
        if (string.IsNullOrWhiteSpace(TestConfig.Current.SqlConnection) ||
            TestConfig.Current.SqlConnection.Contains("YOUR_TEST_DB"))
            Assert.Ignore("Set SqlConnection in testsettings.json to run SQL tests.");
    }

    [TestCaseSource(nameof(Views))]
    public void View_Binds(string view)
    {
        var parts = view.Split('.', 2);
        using var conn = new SqlConnection(TestConfig.Current.SqlConnection);
        conn.Open();
        using var cmd = new SqlCommand($"SELECT TOP 0 * FROM [{parts[0]}].[{parts[1]}];", conn) { CommandTimeout = 30 };

        Assert.DoesNotThrow(() => cmd.ExecuteNonQuery(), $"View {view} is broken");
    }

    [Test]
    public void No_Unresolved_References()
    {
        const string sql = @"
SELECT s.name + '.' + o.name + ' -> ' + d.referenced_entity_name
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE d.referenced_id IS NULL AND d.is_ambiguous = 0
  AND d.referenced_database_name IS NULL
  AND d.referenced_entity_name NOT LIKE '#%';";

        var problems = new List<string>();
        using var conn = new SqlConnection(TestConfig.Current.SqlConnection);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read()) problems.Add(r.GetString(0));

        // Can include false positives (dynamic SQL, objects created at runtime). Warn rather than fail.
        if (problems.Count > 0)
            Assert.Warn("Objects referencing things that don't exist (review):\n  " + string.Join("\n  ", problems));
    }
}
