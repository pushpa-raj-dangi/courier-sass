namespace Courier.Manifests.Domain;

// No "Dispatched" member — this plan's resolved design decision #2: nothing distinguishes a
// dispatched-but-not-yet-in-transit resting state, so the dispatch action moves Sealed straight to
// InTransit.
public enum ManifestStatus
{
    Draft,
    Sealed,
    InTransit,
    Received,
    Closed
}
