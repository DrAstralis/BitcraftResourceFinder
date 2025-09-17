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

    // ?? Add front-end parity filters + paging
    public async Task<IActionResult> Index(string? q, int? tier, Guid? type, Guid? biome, string? status, int page = 1)
    {
        const int pageSize = 20;

        var query = _db.Resources
            .Include(r => r.Type)
            .Include(r => r.Biome)
            .AsQueryable();

        // same filtering semantics as ResourcesController
        if (!string.IsNullOrWhiteSpace(q))
        {
            var cq = SeedData.Canonicalize(q);
            query = query.Where(r => r.CanonicalName.Contains(cq) || r.Name.Contains(q));
        }
        if (tier.HasValue) query = query.Where(r => r.Tier == tier);
        if (type.HasValue) query = query.Where(r => r.TypeId == type);
        if (biome.HasValue) query = query.Where(r => r.BiomeId == biome);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)) query = query.Where(r => r.Status == ResourceStatus.Confirmed);
            if (status.Equals("unconfirmed", StringComparison.OrdinalIgnoreCase)) query = query.Where(r => r.Status == ResourceStatus.Unconfirmed);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)   // keep newest-first for admin review
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // supply data for dropdowns and echo filters back to the view
        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
        ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = pageSize;
        ViewBag.Query = q; ViewBag.Tier = tier; ViewBag.Type = type; ViewBag.Biome = biome; ViewBag.Status = status;

        return View(items);
    }

    [HttpPost("/admin/resources/{id}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, string status, string? returnUrl = null)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        r.Status = status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
            ? ResourceStatus.Confirmed
            : ResourceStatus.Unconfirmed;

        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction("Index");
    }


    [HttpGet("/admin/resources/{id}/edit")]
    public async Task<IActionResult> Edit(Guid id, string? returnUrl = null)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
        ViewBag.ReturnUrl = returnUrl; // keep it for the form + Cancel
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
    IFormFile? file = null,
    string? returnUrl = null)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        // Apply posted fields so the view re-renders with user input on error
        r.Tier = tier;
        r.TypeId = typeId;
        r.BiomeId = biomeId;
        r.Name = (name ?? string.Empty).Trim();
        r.CanonicalName = Models.SeedData.Canonicalize(r.Name);

        if (removeImage)
        {
            await _img.MoveToDeleteAsync(id);
            r.Img256Url = null;
            r.Img512Url = null;
            r.ImagePhash = null;
        }

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
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
            ViewBag.ReturnUrl = returnUrl;
            return View("Edit", r);
        }

        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["AlertSuccess"] = "Resource updated.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction("Index");
    }

    [HttpPost("/admin/resources/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnUrl = null)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        try { await _img.MoveToDeleteAsync(id); } catch { /* swallow */ }

        _db.Resources.Remove(r);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction("Index");
    }


}
