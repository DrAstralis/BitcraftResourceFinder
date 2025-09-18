using System.Net;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class ImageProxyController : ControllerBase
{
    private static readonly string[] AllowedPrefixes = new[] { "http://", "https://" };
    private const long MaxBytes = 50L * 1024 * 1024; // 50 MB cap

    [HttpGet("/image-proxy")]
    public async Task<IActionResult> Get([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !AllowedPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return BadRequest("Invalid URL.");

        using var http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        http.Timeout = TimeSpan.FromSeconds(12);

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, "Fetch failed.");
        var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!ct.StartsWith("image/")) return BadRequest("Not an image.");
        var len = resp.Content.Headers.ContentLength ?? 0;
        if (len > MaxBytes) return BadRequest("Image too large.");

        var stream = await resp.Content.ReadAsStreamAsync();
        return File(stream, ct);
    }
}
