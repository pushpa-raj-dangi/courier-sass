namespace Courier.International.Application.Carriers;

public interface ICarrierProviderRegistry
{
    ICarrierProvider? Find(string carrierCode);
    IReadOnlyCollection<string> RegisteredCodes { get; }
}

// Scoped: providers built on typed HttpClients are transient, so the registry is rebuilt per scope.
public sealed class CarrierProviderRegistry : ICarrierProviderRegistry
{
    private readonly Dictionary<string, ICarrierProvider> _providers;

    public CarrierProviderRegistry(IEnumerable<ICarrierProvider> providers)
    {
        _providers = new Dictionary<string, ICarrierProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.CarrierCode, provider))
            {
                throw new InvalidOperationException($"Two carrier providers are registered for code '{provider.CarrierCode}'.");
            }
        }
    }

    public ICarrierProvider? Find(string carrierCode) => _providers.GetValueOrDefault(carrierCode);

    public IReadOnlyCollection<string> RegisteredCodes => _providers.Keys.ToList();
}
