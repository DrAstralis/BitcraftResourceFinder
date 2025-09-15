using Bitcraft.ResourceFinder.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;

namespace Bitcraft.ResourceFinder.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ImageService _img;
    public AdminController(AppDbContext db, ImageService img) { _db = db; _img = img; }

    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 20;

        var query = _db.Resources
            .Include(r => r.Type)
            .Include(r => r.Biome)
            .AsQueryable();

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        return View(items);
    }

    [HttpPost("/admin/resources/{id}/status")]
    public async Task<IActionResult> SetStatus(Guid id, string status)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        r.Status = status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
            ? ResourceStatus.Confirmed
            : ResourceStatus.Unconfirmed;

        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    [HttpGet("/admin/resources/{id}/edit")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
        return View(r);
    }

    [HttpPost("/admin/resources/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(
        Guid id,
        int tier,
        Guid typeId,
        Guid biomeId,
        string name,
        bool removeImage = false,
        IFormFile? file = null)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        // Apply posted fields so the view re-renders with user input on error
        r.Tier = tier;
        r.TypeId = typeId;
        r.BiomeId = biomeId;
        r.Name = (name ?? string.Empty).Trim();
        r.CanonicalName = Models.SeedData.Canonicalize(r.Name);

        // Handle image removal first
        if (removeImage)
        {
            r.Img256Url = null;
            r.Img512Url = null;
            r.ImagePhash = null;
        }

        // If a new file was provided, try to process it
        if (file is { Length: > 0 })
        {
            try
            {
                var (img256, img512, phash) = await _img.ProcessAndSaveAsync(file, id);
                r.Img256Url = img256;
                r.Img512Url = img512;
                r.ImagePhash = phash;
            }
            catch (InvalidOperationException ex)
            {
                // e.g. "Image too large (max 300 KB)." or "Unsupported image type."
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        if (!ModelState.IsValid)
        {
            // Re-populate dropdowns and return the same Edit view with validation messages
            ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
            return View("Edit", r);
        }

        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["AlertSuccess"] = "Resource updated.";
        return RedirectToAction("Index");
    }

    [HttpPost("/admin/resources/{id}/delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        _db.Resources.Remove(r);
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }
}
