namespace Hms.SharedKernel.Performance;

/// <summary>
/// Standard cache profiles for HMS API responses.
/// Applied via [ResponseCache(CacheProfileName = "...")] on endpoints.
/// </summary>
public static class CacheProfiles
{
    public const string ShortLived = "ShortLived";   // 30s — patient list, encounter list
    public const string MediumLived = "MediumLived"; // 5min — reference data, facility info
    public const string NoCache = "NoCache";         // 0s — mutations, sensitive data

    public static void Configure(IDictionary<string, Microsoft.AspNetCore.Mvc.CacheProfile> profiles)
    {
        profiles[ShortLived] = new() { Duration = 30, VaryByQueryKeys = ["skip", "take", "tenantId"] };
        profiles[MediumLived] = new() { Duration = 300, VaryByQueryKeys = ["tenantId"] };
        profiles[NoCache] = new() { Duration = 0, NoStore = true };
    }
}