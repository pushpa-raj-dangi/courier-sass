using System.Text.Json.Serialization;

namespace Courier.Manifests.Domain;

// Forward is the default (see ManifestConfiguration's HasDefaultValue and
// CreateManifestRequest's default parameter) — every manifest before Phase 9 was implicitly this.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManifestPurpose
{
    Forward,
    Return
}
