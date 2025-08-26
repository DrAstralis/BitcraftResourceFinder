
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
        var query = _db.Resources.Include(r => r.Type).Include(r => r.Biome).AsQueryable();
        if (status.Equals("unconfirmed", StringComparison.OrdinalIgnoreCase)) query = query.Where(r => r.Status == ResourceStatus.Unconfirmed);
        if (status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)) query = query.Where(r => r.Status == ResourceStatus.Confirmed);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1)*pageSize).Take(pageSize).ToListAsync();
        ViewBag.Total = total; ViewBag.Page = page; ViewBag.Status = status; ViewBag.PageSize = pageSize;
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
}
