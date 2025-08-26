
namespace Bitcraft.ResourceFinder.Web.Services;

public class ModerationService
{
    // Minimal deny list (extend as needed)
    private static readonly string[] Terms = new[] {
        "nazi","kkk","alt-right","alt right","neo-nazi","white power","swastika","1488","14/88",
        "fuck","shit","bitch","asshole","cunt","bastard","dick","piss"
    };

    public bool ContainsProhibited(string text, out string term)
    {
        var canon = Canon(text);
        foreach (var t in Terms)
        {
            if (canon.Contains(Canon(t)))
            {
                term = t;
                return true;
            }
        }
        term = "";
        return false;
    }

    private static string Canon(string s)
    {
        var n = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in n)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return string.Join(" ", new string(sb.ToString().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
