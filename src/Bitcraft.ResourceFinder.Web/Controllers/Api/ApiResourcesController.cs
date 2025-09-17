using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Data;
using Bitcraft.ResourceFinder.Web.Models;
using Bitcraft.ResourceFinder.Web.Services;

namespace Bitcraft.ResourceFinder.Web.Controllers.Api
{
    [ApiController]
    [Route("api/resources")]
    public class ApiResourcesController : ControllerBase
    {
        private const int MaxBytes = 256 * 1024; // 256 KB
        private const int MaxItems = 200;

        private readonly AppDbContext _db;
        private readonly ImageService _img;
        private readonly ModerationService _mod;

        public ApiResourcesController(AppDbContext db, ImageService img, ModerationService mod)
        {
            _db = db; _img = img; _mod = mod;
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
            var items = await query.OrderBy(r => r.Name).Skip((page - 1) * limit).Take(limit).ToListAsync();
            return Ok(new { items, total, page, limit });
        }

        [Authorize(Roles = "Admin")]
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

        [Authorize]
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

        [Authorize(Roles = "Admin")]
        [HttpPost("bulk-import")]
        [Consumes("application/json")]
        public async Task<IActionResult> BulkImport([FromBody] BulkImportRequest body)
        {
            var errors = new List<string>();
            var rejected = new List<RejectedItem>();
            var accepted = 0;

            // Size guard
            var len = HttpContext.Request.ContentLength;
            if (len.HasValue && len.Value > MaxBytes)
                errors.Add($"Payload too large ({len.Value} bytes). Max {MaxBytes} bytes.");

            // Body/item count guards
            if (body?.Items == null || body.Items.Count == 0)
                errors.Add("No items provided.");
            if (body?.Items?.Count > MaxItems)
                errors.Add($"Too many items ({body.Items.Count}). Max {MaxItems}.");

            if (errors.Count > 0)
                return BadRequest(new BulkImportResult(false, 0, rejected, errors));

            var items = body!.Items; // safe after the guard above

            // Build Type/Biome maps by Name OR Slug (case-insensitive)
            var types = await _db.Types.Select(t => new { t.Id, t.Name, t.Slug }).ToListAsync();
            var biomes = await _db.Biomes.Select(b => new { b.Id, b.Name, b.Slug }).ToListAsync();

            var typeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                if (!string.IsNullOrWhiteSpace(t.Name)) typeMap[t.Name] = t.Id;
                if (!string.IsNullOrWhiteSpace(t.Slug)) typeMap[t.Slug] = t.Id;
            }

            var biomeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in biomes)
            {
                if (!string.IsNullOrWhiteSpace(b.Name)) biomeMap[b.Name] = b.Id;
                if (!string.IsNullOrWhiteSpace(b.Slug)) biomeMap[b.Slug] = b.Id;
            }

            // Intra-request duplicate key set (Tier|TypeId|BiomeId|Canonical)
            var batchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toInsert = new List<Resource>();

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var reasons = new List<string>();

                if (it == null)
                {
                    rejected.Add(new RejectedItem(i, new[] { "Item is null." }));
                    continue;
                }

                // Basic field checks
                var name = (it.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
                    reasons.Add("'name' required (? 80 chars).");

                if (!it.Tier.HasValue || it.Tier < 1 || it.Tier > 10)
                    reasons.Add("'tier' must be 1–10.");

                // Resolve Type
                Guid typeId = default;
                if (string.IsNullOrWhiteSpace(it.Type))
                    reasons.Add("'type' required (use Name or Slug).");
                else if (!typeMap.TryGetValue(it.Type!, out typeId))
                    reasons.Add("'type' not found (use Name or Slug).");

                // Resolve Biome
                Guid biomeId = default;
                if (string.IsNullOrWhiteSpace(it.Biome))
                    reasons.Add("'biome' required (use Name or Slug).");
                else if (!biomeMap.TryGetValue(it.Biome!, out biomeId))
                    reasons.Add("'biome' not found (use Name or Slug).");

                // Profanity (name)
                if (!string.IsNullOrWhiteSpace(name) && _mod.ContainsProhibited(name, out var term))
                    reasons.Add($"Prohibited term in name: {term}");

                if (reasons.Count > 0)
                {
                    rejected.Add(new RejectedItem(i, reasons));
                    continue;
                }

                var canonical = SeedData.Canonicalize(name);
                var tier = it.Tier!.Value;

                // Intra-batch dup
                var batchKey = $"{tier}|{typeId}|{biomeId}|{canonical}";
                if (!batchKeys.Add(batchKey))
                {
                    rejected.Add(new RejectedItem(i, new[] { "Duplicate within payload (same Tier/Type/Biome/Name)." }));
                    continue;
                }

                // DB dup (Tier+Type+Biome+CanonicalName)
                var exists = await _db.Resources.AnyAsync(r =>
                    r.Tier == tier && r.TypeId == typeId && r.BiomeId == biomeId && r.CanonicalName == canonical);
                if (exists)
                {
                    rejected.Add(new RejectedItem(i, new[] { "Duplicate already exists in database." }));
                    continue;
                }

                toInsert.Add(new Resource
                {
                    Id = Guid.NewGuid(),
                    Tier = tier,
                    Name = name,
                    CanonicalName = canonical,
                    TypeId = typeId,
                    BiomeId = biomeId,
                    Status = ResourceStatus.Unconfirmed,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (toInsert.Count == 0 && rejected.Count == 0)
                errors.Add("Nothing to import.");

            if (errors.Count > 0)
                return BadRequest(new BulkImportResult(false, 0, rejected, errors));

            if (toInsert.Count > 0)
            {
                _db.Resources.AddRange(toInsert);
                await _db.SaveChangesAsync();
                accepted = toInsert.Count;
            }

            // 200 even with partial rejects; UI will display reasons in the same error element
            return Ok(new BulkImportResult(true, accepted, rejected, new()));
        }

        // ===== Request/Response contracts (keep inside the same class) =====

        public record BulkImportRequest
        {
            public List<ImportItem> Items { get; init; } = new();
        }

        public record ImportItem
        {
            public string? Name { get; init; }
            public int? Tier { get; init; }
            public string? Type { get; init; }   // Name or Slug
            public string? Biome { get; init; }  // Name or Slug
        }

        public record RejectedItem(int Index, IEnumerable<string> Reasons);

        // IMPORTANT: keep ONLY these four properties (no extra lowercase duplicates)
        public record BulkImportResult(bool Ok, int Accepted, List<RejectedItem> Rejected, List<string> Errors);
    }
}
