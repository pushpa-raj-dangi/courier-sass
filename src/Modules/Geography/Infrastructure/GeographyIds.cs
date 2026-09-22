using System.Security.Cryptography;
using System.Text;

namespace Courier.Geography.Infrastructure;

// Deterministic Guid generation for seeded reference rows, mirroring
// Courier.Identity.Application.PermissionCatalog.IdFor — the same key always produces the same
// Guid, so HasData rows stay stable across regenerated EF migrations.
public static class GeographyIds
{
    public static Guid CountryId(string isoCode) => IdFor($"country:{isoCode}");

    public static Guid ProvinceId(string province) => IdFor($"province:{province}");

    public static Guid DistrictId(string province, string district) =>
        IdFor($"district:{province}:{district}");

    public static Guid LocalLevelId(string province, string district, string localLevel) =>
        IdFor($"locallevel:{province}:{district}:{localLevel}");

    public static Guid WardId(string province, string district, string localLevel, int wardNumber) =>
        IdFor($"ward:{province}:{district}:{localLevel}:{wardNumber}");

    public static Guid AreaId(string province, string district, string localLevel, int wardNumber, string areaLabel) =>
        IdFor($"area:{province}:{district}:{localLevel}:{wardNumber}:{areaLabel}");

    private static Guid IdFor(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
