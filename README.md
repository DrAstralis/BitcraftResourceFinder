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

Images Config:

Folder name & config

Keep images under wwwroot/images or set "Image:RootPath" accordingly in appsettings. Dev already ships with "Image:RootPath": "wwwroot/images". 

The code above resolves absolute disk path from that setting using WebRoot/ContentRoot.

Static files must be enabled (they are): app.UseStaticFiles(); in Program.cs. Don’t remove/move it after endpoint mapping. 

Write permissions

The hosting process must be able to write to the resolved images folder (e.g., wwwroot/images). Containers/App Services usually allow this; some locked-down IIS sites may need ACLs.

Placeholder image

Make sure wwwroot/images/placeholder-256.webp exists (used when a resource has no image). The view code expects it.

Existing data

Some rows already store URLs like "/images/…"; the ResolveImg() helper handles both "/images/…" and "~/images/…", so you don’t have to backfill the DB.