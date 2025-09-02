
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;
using Bitcraft.ResourceFinder.Web.Services;

namespace Bitcraft.ResourceFinder.Web.Controllers;

public class ResourcesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ModerationService _mod;
    private readonly DuplicateService _dup;
    private readonly ImageService _img;

    public ResourcesController(AppDbContext db, ModerationService mod, DuplicateService dup, ImageService img)
    {
        _db = db; _mod = mod; _dup = dup; _img = img;
    }

    public async Task<IActionResult> Index(string? q, int? tier, Guid? type, Guid? biome, string? status, int page = 1)
    {
        const int pageSize = 20;
        var query = _db.Resources
            .Include(r => r.Type)
            .Include(r => r.Biome)
            .AsQueryable();

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
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = pageSize;
        ViewBag.Query = q; ViewBag.Tier = tier; ViewBag.Type = type; ViewBag.Biome = biome; ViewBag.Status = status;
        return View(items);
    }

    [HttpGet("/resources/new")]
    public async Task<IActionResult> New()
    {
        ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Biomes = await _db.Biomes.OrderBy(t => t.Name).ToListAsync();
        return View();
    }

    [HttpPost("/resources")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int tier, Guid typeId, Guid biomeId, string name, IFormFile? image)
    {
        if (string.IsNullOrWhiteSpace(name) || tier < 1 || tier > 10) return BadRequest("Invalid data.");
        if (await _db.Types.FindAsync(typeId) is null) return BadRequest("Unknown type.");
        if (await _db.Biomes.FindAsync(biomeId) is null) return BadRequest("Unknown biome.");

        if (_mod.ContainsProhibited(name, out var term))
            return BadRequest($"Prohibited term detected: {term}");

        var canonical = SeedData.Canonicalize(name);
        var incoming = new Resource { Tier = tier, Name = name.Trim(), CanonicalName = canonical, TypeId = typeId, BiomeId = biomeId, Status = ResourceStatus.Unconfirmed };

        // Strong duplicate guard
        var candidate = await _db.Resources.FirstOrDefaultAsync(r => r.Tier == tier && r.TypeId == typeId && r.BiomeId == biomeId && r.CanonicalName == canonical);
        if (candidate != null)
        {
            ModelState.AddModelError(string.Empty,
                "That resource already exists for the selected Tier, Type, and Biome.");

            // repopulate dropdowns
            ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();

            // preserve user input
            ViewBag.Tier = tier;
            ViewBag.TypeId = typeId;
            ViewBag.BiomeId = biomeId;
            ViewBag.Name = name;

            return View("New");
        }

        // Save first to get Id
        try
        {
            _db.Resources.Add(incoming);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty,
                "That resource already exists for the selected Tier, Type, and Biome.");

            ViewBag.Types = await _db.Types.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Biomes = await _db.Biomes.OrderBy(b => b.Name).ToListAsync();

            ViewBag.Tier = tier;
            ViewBag.TypeId = typeId;
            ViewBag.BiomeId = biomeId;
            ViewBag.Name = name;

            return View("New");
        }

        if (image != null)
        {
            try
            {
                var (img256, img512, phash) = await _img.ProcessAndSaveAsync(image, incoming.Id);
                incoming.Img256Url = img256; incoming.Img512Url = img512; incoming.ImagePhash = phash;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // ignore image failure for now, but could bubble to UI
                Console.WriteLine(ex.Message);
            }
        }

        TempData["Success"] = "Submitted! An admin will review and confirm.";
        return RedirectToAction("Index");
    }
}
