
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

// Apply migrations (dev-only convenience)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.EnsureSeedAsync(scope.ServiceProvider, app.Configuration);
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
