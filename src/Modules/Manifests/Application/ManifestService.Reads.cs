using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse?> GetByIdAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        return manifest is null ? null : ToResponse(manifest);
    }

    // Meaningful once the manifest is Closed (a null ReceivedAt then IS the missing-shipment
    // exception per this plan's resolved design decision #4) — also usable while a manifest is
    // still InTransit/Received to show which shipments haven't been scanned in yet.
    public async Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var unreceived = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId && ms.ReceivedAt == null)
            .ToListAsync(cancellationToken);

        var result = new List<MissingShipmentResponse>();
        foreach (var manifestShipment in unreceived)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(manifestShipment.ShipmentId, cancellationToken);
            result.Add(new MissingShipmentResponse(manifestShipment.ShipmentId, scanInfo?.Awb ?? string.Empty, manifestShipment.ScannedAt));
        }
        return result;
    }
}
