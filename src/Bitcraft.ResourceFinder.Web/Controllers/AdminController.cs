using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;
using Bitcraft.ResourceFinder.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bitcraft.ResourceFinder.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ImageService _img;
    public AdminController(AppDbContext db, ImageService img) { _db = db; _img = img; }

    public async Task<IActionResult> Index(string? q, int? tier, Guid? type, Guid? biome, string? status, int page = 1)
    {
        const int pageSize = 20;

        // Base query + includes
        var query = _db.Resources
            .Include(r => r.Type)
            .Include(r => r.Biome)
            .AsQueryable();

        // Filters (same semantics as before)
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
            // (If you later want "any", handle it here by skipping status filter when status == "any")
        }

        // Total BEFORE paging
        var total = await query.CountAsync();

        // Gain #1: priority ordering using SQL-translatable subqueries
        var orderedQuery = query
            .OrderByDescending(r => _db.Reports.Count(rep => rep.ResourceId == r.Id && rep.Status == ReportStatus.Open))
            .ThenByDescending(r => _db.PendingImages.Count(p => p.ResourceId == r.Id))
            .ThenByDescending(r => r.CreatedAt);

        // Page
        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Gain #2: badge dictionaries for just this page
        var pageIds = items.Select(r => r.Id).ToList();

        var openReports = await _db.Reports
            .Where(rep => pageIds.Contains(rep.ResourceId) && rep.Status == ReportStatus.Open)
            .GroupBy(rep => rep.ResourceId)
            .Select(g => new { ResourceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ResourceId, x => x.Count);

        var pendingCounts = await _db.PendingImages
            .Where(p => pageIds.Contains(p.ResourceId))
            .GroupBy(p => p.ResourceId)
            .Select(g => new { ResourceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ResourceId, x => x.Count);

        // Original ViewBags (filters UI + echoes)
        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();
        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Query = q;
        ViewBag.Tier = tier;
        ViewBag.Type = type;
        ViewBag.Biome = biome;
        ViewBag.Status = status;

        // New badge ViewBags
        ViewBag.OpenReports = openReports;
        ViewBag.PendingCounts = pendingCounts;

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

        // Keep the new badges on the page
        var pending = await _db.PendingImages
            .Where(p => p.ResourceId == id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        var reports = await _db.Reports
            .Where(rep => rep.ResourceId == id && rep.Status == ReportStatus.Open)
            .OrderBy(rep => rep.CreatedAt)
            .ToListAsync();
        ViewBag.Pending = pending;
        ViewBag.Reports = reports;

        // Restore returnUrl so the form + Cancel can round-trip
        ViewBag.ReturnUrl = returnUrl;

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
            // Original filter dropdowns
            ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();

            // NEW (from refactor): keep page badges visible on error re-render
            ViewBag.Pending = await _db.PendingImages
                .Where(p => p.ResourceId == id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            ViewBag.Reports = await _db.Reports
                .Where(rep => rep.ResourceId == id && rep.Status == ReportStatus.Open)
                .OrderBy(rep => rep.CreatedAt)
                .ToListAsync();

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


    // --- NEW: Accept a pending image as the official image
    [HttpPost("/admin/resources/{id}/accept-pending/{pid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptPending(Guid id, Guid pid, [FromServices] ImageService img)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        var p = await _db.PendingImages.FirstOrDefaultAsync(x => x.Id == pid && x.ResourceId == id);
        if (p == null) return NotFound();

        try
        {
            var (img256, img512, pHash) = await img.PromotePendingAsync(id, pid);
            r.Img256Url = img256;
            r.Img512Url = img512;
            r.ImagePhash = pHash;
            r.UpdatedAt = DateTime.UtcNow;

            // Remove the accepted pending record
            _db.PendingImages.Remove(p);

            await _db.SaveChangesAsync();
            TempData["Success"] = "Pending image accepted.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction("Edit", new { id });
    }


    // --- NEW: Purge all pending images for a resource
    [HttpPost("/admin/resources/{id}/purge-pending")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgePending(Guid id)
    {
        var items = await _db.PendingImages.Where(p => p.ResourceId == id).ToListAsync();
        if (items.Count > 0)
        {
            _db.PendingImages.RemoveRange(items);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction("Edit", new { id });
    }

    // --- NEW: Close a report
    [HttpPost("/admin/reports/{rid}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseReport(Guid rid)
    {
        var rep = await _db.Reports.FindAsync(rid);
        if (rep == null) return NotFound();
        rep.Status = ReportStatus.Closed;
        rep.ResolvedAt = DateTime.UtcNow;
        // rep.ResolvedByUserId = ... could be set using UserManager if injected
        await _db.SaveChangesAsync();
        return RedirectToAction("Edit", new { id = rep.ResourceId });
    }

    // --- NEW: Remove accepted image (optional admin action on edit page)
    [HttpPost("/admin/resources/{id}/remove-accepted-image")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAccepted(Guid id)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        r.Img256Url = null; r.Img512Url = null; r.ImagePhash = null;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction("Edit", new { id });
    }
}
