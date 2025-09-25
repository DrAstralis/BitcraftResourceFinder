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
    public AdminController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string status = "unconfirmed", int page = 1)
    {
        const int pageSize = 20;

        // Base query + filters
        var baseQuery = _db.Resources
            .Include(r => r.Type)
            .Include(r => r.Biome)
            .AsQueryable();

        if (status.Equals("unconfirmed", StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(r => r.Status == ResourceStatus.Unconfirmed);
        if (status.Equals("confirmed", StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(r => r.Status == ResourceStatus.Confirmed);

        var total = await baseQuery.CountAsync();

        // Order by counts using SQL-translatable subqueries
        var orderedQuery = baseQuery
            .OrderByDescending(r => _db.Reports.Count(rep => rep.ResourceId == r.Id && rep.Status == ReportStatus.Open))
            .ThenByDescending(r => _db.PendingImages.Count(p => p.ResourceId == r.Id))
            .ThenByDescending(r => r.CreatedAt);

        // Page
        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Build badge dictionaries for the rows on this page
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

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Status = status;
        ViewBag.OpenReports = openReports;
        ViewBag.PendingCounts = pendingCounts;

        return View(items);
    }


    [HttpPost("/admin/resources/{id}/status")]
    public async Task<IActionResult> SetStatus(Guid id, string status)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        r.Status = status.Equals("confirmed", StringComparison.OrdinalIgnoreCase) ? ResourceStatus.Confirmed : ResourceStatus.Unconfirmed;
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
        ViewBag.Biomes = await _db.Biomes.OrderBy(t => t.Name).ToListAsync();

        var pending = await _db.PendingImages.Where(p => p.ResourceId == id).OrderByDescending(p => p.CreatedAt).ToListAsync();
        var reports = await _db.Reports.Where(rep => rep.ResourceId == id && rep.Status == ReportStatus.Open).OrderBy(rep => rep.CreatedAt).ToListAsync();
        ViewBag.Pending = pending;
        ViewBag.Reports = reports;
        return View(r);
    }

    [HttpPost("/admin/resources/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(Guid id, int tier, Guid typeId, Guid biomeId, string name)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        r.Tier = tier; r.TypeId = typeId; r.BiomeId = biomeId; r.Name = name.Trim(); r.CanonicalName = Models.SeedData.Canonicalize(name);
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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

    // --- NEW: Accept a pending image as the official image
    [HttpPost("/admin/resources/{id}/accept-pending/{pid}")]
    public async Task<IActionResult> AcceptPending(Guid id, Guid pid)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();

        var p = await _db.PendingImages.FirstOrDefaultAsync(x => x.Id == pid && x.ResourceId == id);
        if (p == null) return NotFound();

        // promote to accepted
        r.Img256Url = p.Img256Url;
        r.Img512Url = p.Img512Url;
        r.ImagePhash = p.ImagePhash;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // purge all pending after acceptance (per your instruction to add a purge button; we do both options)
        return RedirectToAction("Edit", new { id });
    }

    // --- NEW: Purge all pending images for a resource
    [HttpPost("/admin/resources/{id}/purge-pending")]
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
