namespace Courier.Manifests.Domain;

// Forward is the default (see ManifestConfiguration's HasDefaultValue and
// CreateManifestRequest's default parameter) — every manifest before Phase 9 was implicitly this.
public enum ManifestPurpose
{
    Forward,
    Return
}
