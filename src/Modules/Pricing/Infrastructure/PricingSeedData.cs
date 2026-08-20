using Courier.Pricing.Domain;

namespace Courier.Pricing.Infrastructure;

// Default seeded reference data. WeightSlabs is a reasonable default ladder, not a hard business
// requirement — easy to challenge/adjust later; org-specific pricing is fully expressed via
// different ZoneRate.BaseRate values per slab, not different slab boundaries (WeightSlab is
// shared/global by design, see this plan's Architecture section).
public static class PricingSeedData
{
    public static readonly (ServiceTypeCode Code, string Name)[] ServiceTypes =
    {
        (ServiceTypeCode.Standard, "Standard"),
        (ServiceTypeCode.Express, "Express"),
        (ServiceTypeCode.SameDay, "Same Day"),
        (ServiceTypeCode.Heavy, "Heavy"),
        (ServiceTypeCode.Special, "Special"),
    };

    public static readonly (decimal MinWeightKg, decimal? MaxWeightKg)[] WeightSlabs =
    {
        (0m, 0.5m),
        (0.5m, 1m),
        (1m, 2m),
        (2m, 5m),
        (5m, 10m),
        (10m, null),
    };
}
