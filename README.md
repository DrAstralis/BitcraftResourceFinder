# Bitcraft Resource Finder (ASP.NET Core 9 MVC)

Public (no-login) submission of Bitcraft base resources + Admin confirmation workflow.

## Quick start (Dev)
1. Install **.NET 9 SDK** and **PostgreSQL**.
2. Copy `src/Bitcraft.ResourceFinder.Web/appsettings.Development.sample.json` → `appsettings.Development.json` and fill in your DB connection + seed admin.
3. From the project folder: `dotnet restore`
4. Create DB schema: `dotnet ef database update`
5. Run: `dotnet run` (or press F5 in Visual Studio)

### Default endpoints
- Public list: `/resources`
- Public add: `/resources/new`
- Admin login: `/Identity/Account/Login`
- Admin dashboard: `/admin`

### Notes
- Public users can submit new resources (Unconfirmed). Admins must log in to confirm/edit/delete/import.
- Images: upload ≤300 KB (jpeg/png/webp). Server stores 256px and 512px **WebP**, strips EXIF, and computes a perceptual hash for duplicate prevention.
- Fuzzy search uses canonical names and can be extended with Postgres `pg_trgm` if enabled.

See `docs/` for API and data model details.