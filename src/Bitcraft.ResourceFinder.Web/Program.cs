
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;
using Bitcraft.ResourceFinder.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core + Postgres
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Identity (Admins only)
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
}).AddRoles<IdentityRole>()
  .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// App services
builder.Services.AddScoped<ImageService>();
builder.Services.AddSingleton<ModerationService>();
builder.Services.AddSingleton<DuplicateService>();

var app = builder.Build();

// Optional migrate + seed (controlled by config; no dev-env tie)
var cfg = app.Configuration;

if (cfg.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Seeding is allowed without schema changes; it will no-op if tables are missing
if (cfg.GetValue<bool>("Database:SeedOnStartup", true))
{
    await SeedData.EnsureSeedAsync(app.Services, cfg);
}


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Public routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Resources}/{action=Index}/{id?}");

// Identity UI (login/register pages)
app.MapRazorPages();

app.Run();
