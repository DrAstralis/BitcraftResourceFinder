
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Bitcraft.ResourceFinder.Web.Services;

public class ImageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public ImageService(IWebHostEnvironment env, IConfiguration cfg)
    {
        _env = env; _cfg = cfg;
    }

    public async Task<(string? img256, string? img512, string? pHash)> ProcessAndSaveAsync(IFormFile file, Guid resourceId)
    {
        await MoveToDeleteAsync(resourceId);

        if (file == null || file.Length == 0) return (null, null, null);
        if (file.Length > 300 * 1024) throw new InvalidOperationException("Image too large (max 300 KB).");

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType.ToLower())) throw new InvalidOperationException("Unsupported image type.");

        using var img = await Image.LoadAsync(file.OpenReadStream()); // single-frame load

        // Resolve web root in any host (debug, IIS, Kestrel, container, etc.)
        var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // Read configured folder but normalize it to live UNDER webRoot
        // Accepts values like "images", "~/images", "/images", or "wwwroot/images"
        var configured = _cfg["Image:RootPath"];
        string imagesFolder;
        if (string.IsNullOrWhiteSpace(configured))
        {
            imagesFolder = Path.Combine(webRoot, "images");
        }
        else if (Path.IsPathRooted(configured))
        {
            // If someone set an absolute path, use it as-is (advanced scenarios)
            imagesFolder = configured;
        }
        else
        {
            var trimmed = configured.TrimStart('~', '/', '\\');
            if (trimmed.StartsWith("wwwroot", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring("wwwroot".Length).TrimStart('/', '\\');

            imagesFolder = Path.Combine(webRoot, trimmed);
        }

        Directory.CreateDirectory(imagesFolder);

        var baseName = resourceId.ToString("N");
        var dest256 = Path.Combine(imagesFolder, baseName + "-256.webp");
        var dest512 = Path.Combine(imagesFolder, baseName + "-512.webp");

        // Resize and save as WebP
        using (var clone = img.Clone(i => i.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(256, 256) })))
            await clone.SaveAsWebpAsync(dest256, new WebpEncoder { Quality = 80 });

        using (var clone = img.Clone(i => i.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(512, 512) })))
            await clone.SaveAsWebpAsync(dest512, new WebpEncoder { Quality = 80 });

        // Return app-relative URLs so PathBase is honored (views will call Url.Content)
        var urlBase = "~/images/" + baseName;
        var img256Url = urlBase + "-256.webp";
        var img512Url = urlBase + "-512.webp";

        string pHash = await ComputeAverageHashAsync(dest512);
        return (img256Url, img512Url, pHash);
    }
    public Task MoveToDeleteAsync(Guid resourceId)
    {
        // Resolve the same folder used by ProcessAndSaveAsync
        var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");

        var configured = _cfg["Image:RootPath"];
        string imagesFolder;
        if (string.IsNullOrWhiteSpace(configured))
        {
            imagesFolder = Path.Combine(webRoot, "images");
        }
        else if (Path.IsPathRooted(configured))
        {
            imagesFolder = configured;
        }
        else
        {
            var trimmed = configured.TrimStart('~', '/', '\\');
            if (trimmed.StartsWith("wwwroot", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring("wwwroot".Length).TrimStart('/', '\\');

            imagesFolder = Path.Combine(webRoot, trimmed);
        }

        Directory.CreateDirectory(imagesFolder);

        var baseName = resourceId.ToString("N");
        var src256 = Path.Combine(imagesFolder, baseName + "-256.webp");
        var src512 = Path.Combine(imagesFolder, baseName + "-512.webp");

        var toDelete = Path.Combine(imagesFolder, "ToDelete");
        Directory.CreateDirectory(toDelete);

        MoveIfExists(src256, Path.Combine(toDelete, Path.GetFileName(src256)));
        MoveIfExists(src512, Path.Combine(toDelete, Path.GetFileName(src512)));

        return Task.CompletedTask;
    }

    private static void MoveIfExists(string src, string dest)
    {
        if (!File.Exists(src)) return;

        // Ensure uniqueness if a file with the same name is already quarantined
        if (File.Exists(dest))
        {
            var dir = Path.GetDirectoryName(dest)!;
            var name = Path.GetFileNameWithoutExtension(dest);
            var ext = Path.GetExtension(dest);
            dest = Path.Combine(dir, $"{name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}{ext}");
        }

        File.Move(src, dest);
    }


    private async Task<string> ComputeAverageHashAsync(string path)
    {
        using var img = await Image.LoadAsync<Rgba32>(path);
        img.Mutate(x => x.Resize(new Size(8, 8)).Grayscale());

        var pixels = new List<byte>(64);
        double sum = 0;

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    byte v = (byte)((p.R + p.G + p.B) / 3);
                    pixels.Add(v);
                    sum += v;
                }
            }
        });

        var avg = sum / pixels.Count;
        ulong hash = 0;
        for (int i = 0; i < pixels.Count; i++)
            if (pixels[i] >= avg) hash |= 1UL << i;

        return hash.ToString("X16");
    }
}
