
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitcraft.ResourceFinder.Web.Models;

public static class SeedData
{
    private static readonly string[] BIOMES = new[] {
        "Safe Meadows","Grasslands","Calm Forest","Maple Forest","Pine Forest",
        "Misty Tundra","Rocky Garden","Swamp","Desert","Snowy Peaks","Jungle","Sawoods","Ocean"
    };

    private static readonly string[] TYPES = new[] {
        "Tree","Flower","Ore Vein","Sand","Mushroom","Fiber Plant","Rock Boulder","Research","Rock Outcrop","Clay","Huntable Animal"
    };

    public static async Task EnsureSeedAsync(IServiceProvider sp, IConfiguration cfg)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed Types
        foreach (var name in TYPES)
        {
            if (!await db.Types.AnyAsync(t => t.Name == name))
            {
                db.Types.Add(new TypeItem { Name = name, Slug = Slugify(name) });
            }
        }
        // Seed Biomes
        foreach (var name in BIOMES)
        {
            if (!await db.Biomes.AnyAsync(t => t.Name == name))
            {
                db.Biomes.Add(new Biome { Name = name, Slug = Slugify(name) });
            }
        }
        await db.SaveChangesAsync();

        // Roles
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleMgr.RoleExistsAsync("Admin"))
            await roleMgr.CreateAsync(new IdentityRole("Admin"));

        // Admin user
        var adminEmail = cfg["Seed:AdminEmail"] ?? "admin@example.com";
        var adminPassword = cfg["Seed:AdminPassword"] ?? "ChangeMe!123";

        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var admin = await userMgr.FindByEmailAsync(adminEmail);
        if (admin == null)
        {
            admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var res = await userMgr.CreateAsync(admin, adminPassword);
            if (res.Succeeded)
            {
                await userMgr.AddToRoleAsync(admin, "Admin");
            }
        }
    }

    public static string Slugify(string s)
    {
        var t = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in t)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var str = new string(sb.ToString().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray());
        return string.Join("-", str.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Canonicalize(string s)
    {
        var n = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in n)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return string.Join(" ", new string(sb.ToString().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
