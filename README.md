# NetAppInventory

Manifest-driven integration tests for ASP.NET MVC apps (.NET Framework 4.8 or .NET 10). It's handy for checking nothing broke during a framework migration.

- **AppInventory**: scans a compiled web DLL, its `.cshtml` views and the SQL Server schema, then writes a manifest (structure only, no data).
- **WebApp.IntegrationTests**: NUnit + Playwright. One smoke test per GET route, SQL view health checks, plus example UI tests.

## Setup (once)
1. In `AppInventory/inventorysettings.json`, set your DLL path, Views folder and test DB.
2. In `WebApp.IntegrationTests/testsettings.json`, set your site URL and test DB.
3. Optional: keep your local values out of git:
   ```powershell
   git update-index --skip-worktree AppInventory/inventorysettings.json WebApp.IntegrationTests/testsettings.json WebApp.IntegrationTests/route-samples.json
   ```

## Use
1. Run **AppInventory** (F5 or double-click). It writes the manifests into `WebApp.IntegrationTests/manifests/`.
2. Start your site.
3. Run the tests from Test Explorer, or with `dotnet test --filter Category=Smoke` (or `Sql` / `UI`).

Command-line arguments still work and override the settings file: `AppInventory --help`.
