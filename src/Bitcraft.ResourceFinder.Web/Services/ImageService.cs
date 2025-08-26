
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
        if (file == null || file.Length == 0) return (null, null, null);
        if (file.Length > 300 * 1024) throw new InvalidOperationException("Image too large (max 300 KB).");

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType.ToLower())) throw new InvalidOperationException("Unsupported image type.");

        using var img = await Image.LoadAsync(file.OpenReadStream());
        // deny animated by enforcing a single frame in ImageSharp load

        // Output folder
        var rootRel = _cfg["Image:RootPath"] ?? "wwwroot/images";
        var root = Path.Combine(AppContext.BaseDirectory, rootRel);
        Directory.CreateDirectory(root);

        var baseName = resourceId.ToString("N");
        var dest256 = Path.Combine(root, baseName + "-256.webp");
        var dest512 = Path.Combine(root, baseName + "-512.webp");

        // Resize and save as WebP
        using (var clone = img.Clone(i => i.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(256, 256) })))
        {
            await clone.SaveAsWebpAsync(dest256, new WebpEncoder { Quality = 80 });
        }
        using (var clone = img.Clone(i => i.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(512, 512) })))
        {
            await clone.SaveAsWebpAsync(dest512, new WebpEncoder { Quality = 80 });
        }

        var urlBase = "/images/" + baseName;
        var img256Url = urlBase + "-256.webp";
        var img512Url = urlBase + "-512.webp";

        // Simple perceptual hash placeholder (average hash)
        string pHash = await ComputeAverageHashAsync(dest512);

        return (img256Url, img512Url, pHash);
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
