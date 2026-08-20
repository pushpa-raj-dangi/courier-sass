using System.Security.Cryptography;
using System.Text;
using Courier.Pricing.Domain;

namespace Courier.Pricing.Infrastructure;

// Deterministic Guid generation for seeded reference rows, mirroring
// Courier.Geography.Infrastructure.GeographyIds — the same key always produces the same Guid, so
// HasData rows stay stable across regenerated EF migrations.
public static class PricingIds
{
    public static Guid ServiceTypeId(ServiceTypeCode code) => IdFor($"servicetype:{code}");

    public static Guid WeightSlabId(decimal minWeightKg, decimal? maxWeightKg) =>
        IdFor($"weightslab:{minWeightKg}:{(maxWeightKg?.ToString() ?? "open")}");

    private static Guid IdFor(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
