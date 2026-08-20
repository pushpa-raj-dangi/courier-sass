namespace Courier.Pricing.Domain;

// Shared/global reference data. MaxWeightKg is nullable to express an open-ended top slab
// (e.g. "10kg+"). A weight w belongs to a slab iff MinWeightKg <= w AND (MaxWeightKg is null OR
// w < MaxWeightKg).
public class WeightSlab
{
    public Guid Id { get; set; }
    public decimal MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
}
