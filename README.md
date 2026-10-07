# NetAppInventory

Manifest-driven integration tests for ASP.NET MVC apps (.NET Framework 4.8 or .NET 10). It's handy for checking nothing broke during a framework migration.

- **AppInventory**: scans a compiled web DLL, its `.cshtml` views and the SQL Server schema, then writes a `manifest.json` (structure only, no data).
- **WebApp.IntegrationTests**: NUnit + Playwright. One smoke test per GET route, SQL view health checks, plus example UI tests.

## Use
```powershell
# 1. Generate manifests
AppInventory --assembly <bin\MyApp.dll> --views <Views folder> --out manifest-app.json
AppInventory --sql "Server=.;Database=<TEST DB>;Integrated Security=True;TrustServerCertificate=True" --check-sql-health --out manifest-sql.json

# 2. Copy both into WebApp.IntegrationTests\manifests\, set BaseUrl/SqlConnection in testsettings.json

# 3. Start the site, then:
dotnet test --filter Category=Smoke   # or Sql / UI
```
