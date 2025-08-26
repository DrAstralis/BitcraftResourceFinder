
using Bitcraft.ResourceFinder.Web.Models;

namespace Bitcraft.ResourceFinder.Web.Services;

public class DuplicateService
{
    public static int Levenshtein(string a, string b)
    {
        var m = a.Length; var n = b.Length;
        var dp = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) dp[i,0] = i;
        for (int j = 0; j <= n; j++) dp[0,j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i,j] = Math.Min(Math.Min(dp[i - 1,j] + 1, dp[i,j - 1] + 1), dp[i - 1,j - 1] + cost);
            }
        return dp[m,n];
    }

    public (bool strong, double score) IsStrongDuplicate(Resource existing, Resource incoming)
    {
        if (existing.Tier != incoming.Tier || existing.TypeId != incoming.TypeId || existing.BiomeId != incoming.BiomeId)
            return (false, 0);
        var dist = Levenshtein(existing.CanonicalName, incoming.CanonicalName);
        var maxLen = Math.Max(existing.CanonicalName.Length, incoming.CanonicalName.Length);
        var score = 1.0 - Math.Min(1.0, (double)dist / Math.Max(1, maxLen));
        return (score >= 0.9, score);
    }
}
