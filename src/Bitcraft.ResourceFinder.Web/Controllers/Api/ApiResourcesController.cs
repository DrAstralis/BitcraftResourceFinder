
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;
using Bitcraft.ResourceFinder.Web.Services;

namespace Bitcraft.ResourceFinder.Web.Controllers.Api;

[ApiController]
[Route("api/resources")]
public class ApiResourcesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ImageService _img;

    public ApiResourcesController(AppDbContext db, ImageService img)
    {
        _db = db; _img = img;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int? tier, [FromQuery] Guid? type, [FromQuery] Guid? biome, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var query = _db.Resources.Include(r => r.Type).Include(r => r.Biome).AsQueryable();
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
        var items = await query.OrderBy(r => r.Name).Skip((page-1)*limit).Take(limit).ToListAsync();
        return Ok(new { items, total, page, limit });
    }

    [Authorize(Roles="Admin")]
    [HttpPost("{id}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] dynamic body)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        string status = body?.status ?? "unconfirmed";
        r.Status = status.Equals("confirmed", StringComparison.OrdinalIgnoreCase) ? ResourceStatus.Confirmed : ResourceStatus.Unconfirmed;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(r);
    }

    [HttpPost("{id}/image")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return NotFound();
        var (img256, img512, phash) = await _img.ProcessAndSaveAsync(file, id);
        r.Img256Url = img256; r.Img512Url = img512; r.ImagePhash = phash; r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(r);
    }
}
