namespace Courier.Cod.Application;

// Cod cannot reference Courier.Shipments.Application directly — Shipments already references Cod
// (it calls into Cod to create COD collections on delivery), so a direct reference the other way
// would be circular. Cod instead declares the lookup it needs here; Shipments.Application
// implements it and Program.cs wires it up, same dependency-inversion shape as any other
// cross-module read in this codebase, just pointed the other way because of who depends on whom.
public interface IShipmentAwbLookupService
{
    Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);
}
