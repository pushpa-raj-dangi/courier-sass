# International I2 — Countries, Country Zones, International Address Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A platform-level `Country` reference table (ISO-3166 alpha-2, seeded, SuperAdmin-toggleable `IsServiced`), an empty `CountryZone` table with admin CRUD, and a standalone `InternationalAddress` value object ready for I3 to consume.

**Architecture:** `Country`, `CountryZone` and `InternationalAddress` join the existing `Geography` module (`src/Modules/Geography/{Domain,Application,Infrastructure,Presentation}`) alongside Province/District/Ward. Reads go through the existing dual-auth (`geography.read` / `merchant.geography.read`) route; admin mutations use the existing `RequireSuperAdmin()` filter — no new permission constants. One additive migration seeds ~217 countries with `IsServiced = false`.

**Tech Stack:** ASP.NET Core / EF Core (PostgreSQL migrations via `dotnet ef`), xUnit (InMemory for unit tests, Postgres for Api tests), Next.js + TanStack Query + Zustand + shadcn/ui + Vitest.

**Spec:** `docs/superpowers/specs/2026-09-22-international-i2-countries-design.md`

## Global Constraints

- `Country`/`CountryZone` are platform-level: no `OrganizationId`, no tenant query filter (same as `FxRate`).
- No `Country` create/delete endpoint. The only mutable fields are `IsServiced` and `CountryZoneId`, via `PATCH /api/v1/platform/countries/{id}`.
- Every seeded `Country` row starts `IsServiced = false`. `CountryZone` ships with zero rows.
- `NameNe` is filled in only for: India (`IN`), China (`CN`), United States (`US`), United Kingdom (`GB`), United Arab Emirates (`AE`), Qatar (`QA`), Australia (`AU`), Canada (`CA`), Malaysia (`MY`), South Korea (`KR`), Saudi Arabia (`SA`), Japan (`JP`). Every other row's `NameNe` is `null`; the UI falls back to `NameEn`.
- No new permission catalog entries. Reads use the existing dual-auth check already in `GeographyEndpoints` (`geography.read` staff / `merchant.geography.read` merchant). All `/platform/countries*` and `/platform/country-zones*` mutations and admin-list reads use `.RequireSuperAdmin()`.
- `InternationalAddress` has zero consumers in this plan — it is a pure value object, format-validated only, no DB access, no check against `Country`/`IsServiced`.
- Error codes: `COUNTRY_VALIDATION_FAILED`, `COUNTRY_ZONE_VALIDATION_FAILED` (both HTTP 400).
- Migration is additive; one migration, no permission-insert migration needed.
- Frontend: shadcn components only; a Sheet is used only for `CountryZone` create/edit (multi-field form) — `Country`'s two mutable fields (`isServiced`, `countryZoneId`) are edited inline in the admin table row (Switch/Select), matching the existing inline-action pattern (`useRetirePlan`'s Retire button), not a detail Sheet.
- Commit trailer on every commit: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Test infra for Api tests: `courier-postgres-1` (5434), `courier-redis-1`, `courier-rabbitmq-1`, `courier-mailhog` must be running. Do not start a second Postgres.

## File Structure

| File | Responsibility |
|---|---|
| `src/Modules/Geography/Domain/Country.cs` (new) | `Country` entity |
| `src/Modules/Geography/Domain/CountryZone.cs` (new) | `CountryZone` entity |
| `src/Modules/Geography/Domain/InternationalAddress.cs` (new) | Value object, format validation only |
| `src/Modules/Geography/Infrastructure/GeographyIds.cs` | + `CountryId` |
| `src/Modules/Geography/Infrastructure/CountrySeedData.cs` (new) | Static seed array + Nepali name overrides |
| `src/Modules/Geography/Infrastructure/CountryConfiguration.cs` (new) | EF config + `HasData` |
| `src/Modules/Geography/Infrastructure/CountryZoneConfiguration.cs` (new) | EF config |
| `src/Persistence/AppDbContext.cs` | + `DbSet<Country>`, `DbSet<CountryZone>`, `ApplyConfiguration` calls |
| `src/Persistence/Migrations/*_AddCountriesAndCountryZones*` (new) | Additive migration (generated) |
| `src/Modules/Geography/Application/CountryDtos.cs` (new) | DTOs + validation exceptions |
| `src/Modules/Geography/Application/CountryQueryService.cs` (new) | `ListServicedAsync`, `ListForAdminAsync` |
| `src/Modules/Geography/Application/CountryAdminService.cs` (new) | `UpdateAsync` (toggle + zone assign) |
| `src/Modules/Geography/Application/CountryZoneService.cs` (new) | `ListAsync`, `CreateAsync`, `UpdateAsync` |
| `src/Modules/Geography/Presentation/GeographyEndpoints.cs` | + `GET /countries` |
| `src/Modules/Geography/Presentation/PlatformCountriesEndpoints.cs` (new) | `/api/v1/platform/countries` |
| `src/Modules/Geography/Presentation/PlatformCountryZonesEndpoints.cs` (new) | `/api/v1/platform/country-zones` |
| `src/Api/Program.cs` | Register services + map new endpoint groups |
| `tests/Geography.Tests/*` | Domain/application unit tests |
| `tests/Api.Tests/PlatformCountriesEndpointsTests.cs`, `.../CountriesGeographyEndpointTests.cs` (new) | API tests |
| `src/web/src/features/platform-countries/*` (new) | Types, hooks, store, sheet |
| `src/web/src/app/platform/countries/page.tsx` (new) | Admin page |
| `src/web/src/app/api/platform/countries/*`, `.../country-zones/*` (new) | Next.js API proxy routes |
| `src/web/src/features/platform/PlatformNav.tsx`, `.test.tsx` | + Countries link |
| `docs/INTERNATIONAL.md`, `docs/DOMAIN-MODEL.md`, `docs/ROADMAP.md` | Status + domain docs |

---

### Task 1: Domain entities and `InternationalAddress`

**Files:**
- Create: `src/Modules/Geography/Domain/Country.cs`
- Create: `src/Modules/Geography/Domain/CountryZone.cs`
- Create: `src/Modules/Geography/Domain/InternationalAddress.cs`
- Modify: `src/Modules/Geography/Infrastructure/GeographyIds.cs`
- Test: `tests/Geography.Tests/InternationalAddressTests.cs`
- Test: `tests/Geography.Tests/GeographyIdsCountryTests.cs`

**Interfaces:**
- Produces: `Country { Guid Id; string IsoCode; string NameEn; string? NameNe; string DefaultCurrency; string CallingCode; bool IsServiced; Guid? CountryZoneId; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }`. `CountryZone { Guid Id; string Name; string? Description; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }`. `InternationalAddress` (sealed record, private ctor): properties `Line1, Line2?, City, Region?, PostalCode?, CountryCode`; `static InternationalAddress Create(string line1, string? line2, string city, string? region, string? postalCode, string countryCode)` throws `InternationalAddressValidationException` when `line1`/`city` are blank or `countryCode` isn't exactly two ASCII letters (normalizes to upper case). `GeographyIds.CountryId(string isoCode)`.

- [ ] **Step 1: Write the failing tests**

`tests/Geography.Tests/InternationalAddressTests.cs`:
```csharp
using Courier.Geography.Domain;
using Xunit;

namespace Courier.Geography.Tests;

public class InternationalAddressTests
{
    [Fact]
    public void Create_ValidInput_NormalizesCountryCodeToUpperCase()
    {
        var address = InternationalAddress.Create("221B Baker St", null, "London", null, "NW1 6XE", "gb");

        Assert.Equal("GB", address.CountryCode);
        Assert.Equal("221B Baker St", address.Line1);
        Assert.Null(address.Line2);
    }

    [Theory]
    [InlineData("", "London", "GB")]
    [InlineData(" ", "London", "GB")]
    public void Create_BlankLine1_Throws(string line1, string city, string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create(line1, null, city, null, null, countryCode));

    [Theory]
    [InlineData("221B Baker St", "", "GB")]
    [InlineData("221B Baker St", " ", "GB")]
    public void Create_BlankCity_Throws(string line1, string city, string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create(line1, null, city, null, null, countryCode));

    [Theory]
    [InlineData("G")]
    [InlineData("GBR")]
    [InlineData("G1")]
    [InlineData("")]
    public void Create_InvalidCountryCode_Throws(string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create("221B Baker St", null, "London", null, null, countryCode));
}
```

`tests/Geography.Tests/GeographyIdsCountryTests.cs`:
```csharp
using Courier.Geography.Infrastructure;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographyIdsCountryTests
{
    [Fact]
    public void CountryId_IsDeterministic() =>
        Assert.Equal(GeographyIds.CountryId("NP"), GeographyIds.CountryId("NP"));

    [Fact]
    public void CountryId_DiffersFromOtherIdKinds() =>
        Assert.NotEqual(GeographyIds.CountryId("NP"), GeographyIds.ProvinceId("NP"));

    [Fact]
    public void CountryId_DiffersAcrossCodes() =>
        Assert.NotEqual(GeographyIds.CountryId("NP"), GeographyIds.CountryId("IN"));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Geography.Tests --filter "FullyQualifiedName~InternationalAddressTests|FullyQualifiedName~GeographyIdsCountryTests" --nologo -v q`
Expected: build errors (`Country`/`InternationalAddress`/`CountryId` don't exist).

- [ ] **Step 3: Implement**

`src/Modules/Geography/Domain/Country.cs`:
```csharp
namespace Courier.Geography.Domain;

// Platform-level reference data (no OrganizationId, no tenant query filter) — same reasoning as
// Pricing's FxRate. The row set is fixed to the seeded ISO-3166-1 alpha-2 list (see
// CountrySeedData); only IsServiced and CountryZoneId are ever mutated after seeding, via
// CountryAdminService. No create/delete API exists for this entity.
public class Country
{
    public Guid Id { get; set; }
    public required string IsoCode { get; set; }
    public required string NameEn { get; set; }
    public string? NameNe { get; set; }
    public required string DefaultCurrency { get; set; }
    public required string CallingCode { get; set; }
    public bool IsServiced { get; set; }
    public Guid? CountryZoneId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/Modules/Geography/Domain/CountryZone.cs`:
```csharp
namespace Courier.Geography.Domain;

// Platform-level (no OrganizationId) grouping of countries for future pricing/reporting (I5).
// Ships with zero rows — a SuperAdmin defines zones and assigns countries via the admin API.
public class CountryZone
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/Modules/Geography/Domain/InternationalAddress.cs`:
```csharp
namespace Courier.Geography.Domain;

public class InternationalAddressValidationException : Exception
{
    public InternationalAddressValidationException(string message) : base(message) { }
}

// Foreign postal address for a future international shipment recipient/sender (I3 — no consumer
// yet). Format validation only: no DB access, and no check that CountryCode exists in Country or
// that the country IsServiced — that policy belongs to whatever books a shipment.
public sealed record InternationalAddress
{
    public string Line1 { get; }
    public string? Line2 { get; }
    public string City { get; }
    public string? Region { get; }
    public string? PostalCode { get; }
    public string CountryCode { get; }

    private InternationalAddress(string line1, string? line2, string city, string? region, string? postalCode, string countryCode)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public static InternationalAddress Create(string line1, string? line2, string city, string? region, string? postalCode, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(line1))
        {
            throw new InternationalAddressValidationException("Line1 is required.");
        }
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new InternationalAddressValidationException("City is required.");
        }
        if (countryCode is not { Length: 2 } || !countryCode.All(char.IsAsciiLetter))
        {
            throw new InternationalAddressValidationException("CountryCode must be a two-letter country code.");
        }

        return new InternationalAddress(line1, line2, city, region, postalCode, countryCode.ToUpperInvariant());
    }
}
```

In `GeographyIds.cs`, add alongside the other `*Id` methods:
```csharp
    public static Guid CountryId(string isoCode) => IdFor($"country:{isoCode}");
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Geography.Tests --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Geography/Domain src/Modules/Geography/Infrastructure/GeographyIds.cs tests/Geography.Tests/InternationalAddressTests.cs tests/Geography.Tests/GeographyIdsCountryTests.cs
git commit -m "feat(geography): add Country, CountryZone and InternationalAddress domain types"
```

---

### Task 2: EF configuration, seed data and migration

**Files:**
- Create: `src/Modules/Geography/Infrastructure/CountrySeedData.cs`
- Create: `src/Modules/Geography/Infrastructure/CountryZoneConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/CountryConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/<timestamp>_AddCountriesAndCountryZones.cs` (+ Designer, snapshot — generated)
- Test: `tests/Geography.Tests/CountrySeedDataTests.cs`

**Interfaces:**
- Consumes: `Country`, `CountryZone` (Task 1), `GeographyIds.CountryId` (Task 1).
- Produces: `CountrySeedData.Countries: (string IsoCode, string NameEn, string DefaultCurrency, string CallingCode)[]`, `CountrySeedData.NameNe: IReadOnlyDictionary<string,string>`. EF `DbSet<Country> Countries`, `DbSet<CountryZone> CountryZones` on `AppDbContext`.

- [ ] **Step 1: Write the failing test**

`tests/Geography.Tests/CountrySeedDataTests.cs`:
```csharp
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class CountrySeedDataTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
            modelBuilder.ApplyConfiguration(new CountryConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public void SeedArray_HasNoDuplicateIsoCodes()
    {
        var codes = CountrySeedData.Countries.Select(c => c.IsoCode).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void SeedArray_EveryIsoCodeIsTwoUpperCaseLetters()
    {
        Assert.All(CountrySeedData.Countries, c =>
        {
            Assert.Equal(2, c.IsoCode.Length);
            Assert.Equal(c.IsoCode, c.IsoCode.ToUpperInvariant());
            Assert.All(c.IsoCode, ch => Assert.True(char.IsAsciiLetterUpper(ch)));
        });
    }

    [Fact]
    public void SeedArray_EveryDefaultCurrencyParsesAsCurrencyCode()
    {
        Assert.All(CountrySeedData.Countries, c =>
            Assert.True(CurrencyCode.TryParse(c.DefaultCurrency, out _), $"{c.IsoCode}: '{c.DefaultCurrency}' is not a valid currency code."));
    }

    [Fact]
    public void SeedArray_EveryCallingCodeStartsWithPlus()
    {
        Assert.All(CountrySeedData.Countries, c => Assert.StartsWith("+", c.CallingCode));
    }

    [Fact]
    public void SeedArray_NepaliNameOverrides_OnlyReferenceCodesInTheMainArray()
    {
        var codes = CountrySeedData.Countries.Select(c => c.IsoCode).ToHashSet();
        Assert.All(CountrySeedData.NameNe.Keys, key => Assert.Contains(key, codes));
    }

    [Theory]
    [InlineData("NP", "NPR", "+977")]
    [InlineData("IN", "INR", "+91")]
    [InlineData("US", "USD", "+1")]
    [InlineData("GB", "GBP", "+44")]
    [InlineData("CN", "CNY", "+86")]
    [InlineData("AU", "AUD", "+61")]
    [InlineData("AE", "AED", "+971")]
    [InlineData("QA", "QAR", "+974")]
    public void SeedArray_KnownCountry_HasExpectedCurrencyAndCallingCode(string isoCode, string currency, string callingCode)
    {
        var country = CountrySeedData.Countries.Single(c => c.IsoCode == isoCode);
        Assert.Equal(currency, country.DefaultCurrency);
        Assert.Equal(callingCode, country.CallingCode);
    }

    [Fact]
    public async Task Seed_PersistsEveryRowWithIsServicedFalseAndNoZone()
    {
        await using var dbContext = CreateSeededContext();

        var count = await dbContext.Set<Country>().CountAsync();
        Assert.Equal(CountrySeedData.Countries.Length, count);
        Assert.True(await dbContext.Set<Country>().AllAsync(c => !c.IsServiced && c.CountryZoneId == null));
    }

    [Fact]
    public async Task Seed_NepalRow_HasNoNepaliNameOverrideButKnownFields()
    {
        await using var dbContext = CreateSeededContext();

        var nepal = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "NP");
        Assert.Equal("Nepal", nepal.NameEn);
        Assert.Null(nepal.NameNe);
        Assert.Equal("NPR", nepal.DefaultCurrency);
    }

    [Fact]
    public async Task Seed_IndiaRow_HasNepaliNameOverride()
    {
        await using var dbContext = CreateSeededContext();

        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        Assert.Equal("भारत", india.NameNe);
    }

    [Fact]
    public async Task CountryZones_StartsEmpty()
    {
        await using var dbContext = CreateSeededContext();

        Assert.Equal(0, await dbContext.Set<CountryZone>().CountAsync());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Geography.Tests --filter "FullyQualifiedName~CountrySeedDataTests" --nologo -v q`
Expected: build error, `CountrySeedData`/`CountryConfiguration`/`CountryZoneConfiguration` don't exist.

- [ ] **Step 3: Implement**

`src/Modules/Geography/Infrastructure/CountrySeedData.cs`:
```csharp
namespace Courier.Geography.Infrastructure;

// ISO-3166-1 alpha-2 code, English name, default currency (ISO-4217 format, not membership-
// checked — see SharedKernel.CurrencyCode), calling code. Covers Nepal, every UN member state,
// plus Vatican City, the State of Palestine, Kosovo and Taiwan, plus a set of dependent
// territories/customs areas relevant to international shipping (Hong Kong, Macao, Puerto Rico,
// Greenland, Bermuda, Cayman Islands, Gibraltar, Isle of Man, Jersey, Guernsey, Faroe Islands,
// Guam, American Samoa, US/British Virgin Islands, Aruba, Curaçao, New Caledonia, French
// Polynesia). This is NOT the complete ISO-3166-1 alpha-2 list — uninhabited/unshippable entries
// (Antarctica, Bouvet Island, South Georgia, etc.) are deliberately excluded. More rows can be
// added later via an additive migration; Country has no create API in I2.
public static class CountrySeedData
{
    public static readonly (string IsoCode, string NameEn, string DefaultCurrency, string CallingCode)[] Countries =
    {
        ("NP", "Nepal", "NPR", "+977"),

        // Africa
        ("DZ", "Algeria", "DZD", "+213"), ("AO", "Angola", "AOA", "+244"), ("BJ", "Benin", "XOF", "+229"),
        ("BW", "Botswana", "BWP", "+267"), ("BF", "Burkina Faso", "XOF", "+226"), ("BI", "Burundi", "BIF", "+257"),
        ("CV", "Cabo Verde", "CVE", "+238"), ("CM", "Cameroon", "XAF", "+237"), ("CF", "Central African Republic", "XAF", "+236"),
        ("TD", "Chad", "XAF", "+235"), ("KM", "Comoros", "KMF", "+269"), ("CG", "Congo", "XAF", "+242"),
        ("CD", "Congo (DRC)", "CDF", "+243"), ("CI", "Côte d'Ivoire", "XOF", "+225"), ("DJ", "Djibouti", "DJF", "+253"),
        ("EG", "Egypt", "EGP", "+20"), ("GQ", "Equatorial Guinea", "XAF", "+240"), ("ER", "Eritrea", "ERN", "+291"),
        ("SZ", "Eswatini", "SZL", "+268"), ("ET", "Ethiopia", "ETB", "+251"), ("GA", "Gabon", "XAF", "+241"),
        ("GM", "Gambia", "GMD", "+220"), ("GH", "Ghana", "GHS", "+233"), ("GN", "Guinea", "GNF", "+224"),
        ("GW", "Guinea-Bissau", "XOF", "+245"), ("KE", "Kenya", "KES", "+254"), ("LS", "Lesotho", "LSL", "+266"),
        ("LR", "Liberia", "LRD", "+231"), ("LY", "Libya", "LYD", "+218"), ("MG", "Madagascar", "MGA", "+261"),
        ("MW", "Malawi", "MWK", "+265"), ("ML", "Mali", "XOF", "+223"), ("MR", "Mauritania", "MRU", "+222"),
        ("MU", "Mauritius", "MUR", "+230"), ("MA", "Morocco", "MAD", "+212"), ("MZ", "Mozambique", "MZN", "+258"),
        ("NA", "Namibia", "NAD", "+264"), ("NE", "Niger", "XOF", "+227"), ("NG", "Nigeria", "NGN", "+234"),
        ("RW", "Rwanda", "RWF", "+250"), ("ST", "Sao Tome and Principe", "STN", "+239"), ("SN", "Senegal", "XOF", "+221"),
        ("SC", "Seychelles", "SCR", "+248"), ("SL", "Sierra Leone", "SLE", "+232"), ("SO", "Somalia", "SOS", "+252"),
        ("ZA", "South Africa", "ZAR", "+27"), ("SS", "South Sudan", "SSP", "+211"), ("SD", "Sudan", "SDG", "+249"),
        ("TZ", "Tanzania", "TZS", "+255"), ("TG", "Togo", "XOF", "+228"), ("TN", "Tunisia", "TND", "+216"),
        ("UG", "Uganda", "UGX", "+256"), ("ZM", "Zambia", "ZMW", "+260"), ("ZW", "Zimbabwe", "ZWL", "+263"),
        ("EH", "Western Sahara", "MAD", "+212"),

        // Americas
        ("AG", "Antigua and Barbuda", "XCD", "+1"), ("AR", "Argentina", "ARS", "+54"), ("BS", "Bahamas", "BSD", "+1"),
        ("BB", "Barbados", "BBD", "+1"), ("BZ", "Belize", "BZD", "+501"), ("BO", "Bolivia", "BOB", "+591"),
        ("BR", "Brazil", "BRL", "+55"), ("CA", "Canada", "CAD", "+1"), ("CL", "Chile", "CLP", "+56"),
        ("CO", "Colombia", "COP", "+57"), ("CR", "Costa Rica", "CRC", "+506"), ("CU", "Cuba", "CUP", "+53"),
        ("DM", "Dominica", "XCD", "+1"), ("DO", "Dominican Republic", "DOP", "+1"), ("EC", "Ecuador", "USD", "+593"),
        ("SV", "El Salvador", "USD", "+503"), ("GD", "Grenada", "XCD", "+1"), ("GT", "Guatemala", "GTQ", "+502"),
        ("GY", "Guyana", "GYD", "+592"), ("HT", "Haiti", "HTG", "+509"), ("HN", "Honduras", "HNL", "+504"),
        ("JM", "Jamaica", "JMD", "+1"), ("MX", "Mexico", "MXN", "+52"), ("NI", "Nicaragua", "NIO", "+505"),
        ("PA", "Panama", "PAB", "+507"), ("PY", "Paraguay", "PYG", "+595"), ("PE", "Peru", "PEN", "+51"),
        ("KN", "Saint Kitts and Nevis", "XCD", "+1"), ("LC", "Saint Lucia", "XCD", "+1"),
        ("VC", "Saint Vincent and the Grenadines", "XCD", "+1"), ("SR", "Suriname", "SRD", "+597"),
        ("TT", "Trinidad and Tobago", "TTD", "+1"), ("US", "United States", "USD", "+1"), ("UY", "Uruguay", "UYU", "+598"),
        ("VE", "Venezuela", "VES", "+58"),

        // Asia
        ("AF", "Afghanistan", "AFN", "+93"), ("AM", "Armenia", "AMD", "+374"), ("AZ", "Azerbaijan", "AZN", "+994"),
        ("BH", "Bahrain", "BHD", "+973"), ("BD", "Bangladesh", "BDT", "+880"), ("BT", "Bhutan", "BTN", "+975"),
        ("BN", "Brunei", "BND", "+673"), ("KH", "Cambodia", "KHR", "+855"), ("CN", "China", "CNY", "+86"),
        ("CY", "Cyprus", "EUR", "+357"), ("GE", "Georgia", "GEL", "+995"), ("IN", "India", "INR", "+91"),
        ("ID", "Indonesia", "IDR", "+62"), ("IR", "Iran", "IRR", "+98"), ("IQ", "Iraq", "IQD", "+964"),
        ("IL", "Israel", "ILS", "+972"), ("JP", "Japan", "JPY", "+81"), ("JO", "Jordan", "JOD", "+962"),
        ("KZ", "Kazakhstan", "KZT", "+7"), ("KW", "Kuwait", "KWD", "+965"), ("KG", "Kyrgyzstan", "KGS", "+996"),
        ("LA", "Laos", "LAK", "+856"), ("LB", "Lebanon", "LBP", "+961"), ("MY", "Malaysia", "MYR", "+60"),
        ("MV", "Maldives", "MVR", "+960"), ("MN", "Mongolia", "MNT", "+976"), ("MM", "Myanmar", "MMK", "+95"),
        ("KP", "North Korea", "KPW", "+850"), ("OM", "Oman", "OMR", "+968"), ("PK", "Pakistan", "PKR", "+92"),
        ("PS", "Palestine", "ILS", "+970"), ("PH", "Philippines", "PHP", "+63"), ("QA", "Qatar", "QAR", "+974"),
        ("SA", "Saudi Arabia", "SAR", "+966"), ("SG", "Singapore", "SGD", "+65"), ("KR", "South Korea", "KRW", "+82"),
        ("LK", "Sri Lanka", "LKR", "+94"), ("SY", "Syria", "SYP", "+963"), ("TW", "Taiwan", "TWD", "+886"),
        ("TJ", "Tajikistan", "TJS", "+992"), ("TH", "Thailand", "THB", "+66"), ("TL", "Timor-Leste", "USD", "+670"),
        ("TR", "Turkey", "TRY", "+90"), ("TM", "Turkmenistan", "TMT", "+993"), ("AE", "United Arab Emirates", "AED", "+971"),
        ("UZ", "Uzbekistan", "UZS", "+998"), ("VN", "Vietnam", "VND", "+84"), ("YE", "Yemen", "YER", "+967"),

        // Europe
        ("AL", "Albania", "ALL", "+355"), ("AD", "Andorra", "EUR", "+376"), ("AT", "Austria", "EUR", "+43"),
        ("BY", "Belarus", "BYN", "+375"), ("BE", "Belgium", "EUR", "+32"), ("BA", "Bosnia and Herzegovina", "BAM", "+387"),
        ("BG", "Bulgaria", "BGN", "+359"), ("HR", "Croatia", "EUR", "+385"), ("CZ", "Czechia", "CZK", "+420"),
        ("DK", "Denmark", "DKK", "+45"), ("EE", "Estonia", "EUR", "+372"), ("FI", "Finland", "EUR", "+358"),
        ("FR", "France", "EUR", "+33"), ("DE", "Germany", "EUR", "+49"), ("GR", "Greece", "EUR", "+30"),
        ("HU", "Hungary", "HUF", "+36"), ("IS", "Iceland", "ISK", "+354"), ("IE", "Ireland", "EUR", "+353"),
        ("IT", "Italy", "EUR", "+39"), ("XK", "Kosovo", "EUR", "+383"), ("LV", "Latvia", "EUR", "+371"),
        ("LI", "Liechtenstein", "CHF", "+423"), ("LT", "Lithuania", "EUR", "+370"), ("LU", "Luxembourg", "EUR", "+352"),
        ("MT", "Malta", "EUR", "+356"), ("MD", "Moldova", "MDL", "+373"), ("MC", "Monaco", "EUR", "+377"),
        ("ME", "Montenegro", "EUR", "+382"), ("NL", "Netherlands", "EUR", "+31"), ("MK", "North Macedonia", "MKD", "+389"),
        ("NO", "Norway", "NOK", "+47"), ("PL", "Poland", "PLN", "+48"), ("PT", "Portugal", "EUR", "+351"),
        ("RO", "Romania", "RON", "+40"), ("RU", "Russia", "RUB", "+7"), ("SM", "San Marino", "EUR", "+378"),
        ("RS", "Serbia", "RSD", "+381"), ("SK", "Slovakia", "EUR", "+421"), ("SI", "Slovenia", "EUR", "+386"),
        ("ES", "Spain", "EUR", "+34"), ("SE", "Sweden", "SEK", "+46"), ("CH", "Switzerland", "CHF", "+41"),
        ("UA", "Ukraine", "UAH", "+380"), ("GB", "United Kingdom", "GBP", "+44"), ("VA", "Vatican City", "EUR", "+379"),

        // Oceania
        ("AU", "Australia", "AUD", "+61"), ("FJ", "Fiji", "FJD", "+679"), ("KI", "Kiribati", "AUD", "+686"),
        ("MH", "Marshall Islands", "USD", "+692"), ("FM", "Micronesia", "USD", "+691"), ("NR", "Nauru", "AUD", "+674"),
        ("NZ", "New Zealand", "NZD", "+64"), ("PW", "Palau", "USD", "+680"), ("PG", "Papua New Guinea", "PGK", "+675"),
        ("WS", "Samoa", "WST", "+685"), ("SB", "Solomon Islands", "SBD", "+677"), ("TO", "Tonga", "TOP", "+676"),
        ("TV", "Tuvalu", "AUD", "+688"), ("VU", "Vanuatu", "VUV", "+678"),

        // Dependent territories / customs areas relevant to international shipping
        ("HK", "Hong Kong", "HKD", "+852"), ("MO", "Macao", "MOP", "+853"), ("PR", "Puerto Rico", "USD", "+1"),
        ("GL", "Greenland", "DKK", "+299"), ("BM", "Bermuda", "BMD", "+1"), ("KY", "Cayman Islands", "KYD", "+1"),
        ("GI", "Gibraltar", "GIP", "+350"), ("IM", "Isle of Man", "GBP", "+44"), ("JE", "Jersey", "GBP", "+44"),
        ("GG", "Guernsey", "GBP", "+44"), ("FO", "Faroe Islands", "DKK", "+298"), ("GU", "Guam", "USD", "+1"),
        ("AS", "American Samoa", "USD", "+1"), ("VI", "United States Virgin Islands", "USD", "+1"),
        ("VG", "British Virgin Islands", "USD", "+1"), ("AW", "Aruba", "AWG", "+297"), ("CW", "Curaçao", "ANG", "+599"),
        ("NC", "New Caledonia", "XPF", "+687"), ("PF", "French Polynesia", "XPF", "+689"),
    };

    // Nepali names for Nepal's most likely early trading-partner destinations (approved 2026-09-22
    // brainstorm decision). Every other row's NameNe is null; the UI falls back to NameEn.
    public static readonly IReadOnlyDictionary<string, string> NameNe = new Dictionary<string, string>
    {
        ["IN"] = "भारत",
        ["CN"] = "चीन",
        ["US"] = "संयुक्त राज्य अमेरिका",
        ["GB"] = "संयुक्त अधिराज्य",
        ["AE"] = "संयुक्त अरब इमिरेट्स",
        ["QA"] = "कतार",
        ["AU"] = "अस्ट्रेलिया",
        ["CA"] = "क्यानाडा",
        ["MY"] = "मलेसिया",
        ["KR"] = "दक्षिण कोरिया",
        ["SA"] = "साउदी अरब",
        ["JP"] = "जापान",
    };
}
```

`src/Modules/Geography/Infrastructure/CountryZoneConfiguration.cs`:
```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class CountryZoneConfiguration : IEntityTypeConfiguration<CountryZone>
{
    public void Configure(EntityTypeBuilder<CountryZone> builder)
    {
        builder.ToTable("CountryZones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(z => z.Name).IsUnique();
        builder.Property(z => z.Description).HasMaxLength(500);
    }
}
```

`src/Modules/Geography/Infrastructure/CountryConfiguration.cs`:
```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    // HasData rows need a fixed value for every column — DateTimeOffset.UtcNow here would make EF
    // regenerate this migration's snapshot diff on every build.
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.IsoCode).HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.IsoCode).IsUnique();
        builder.Property(c => c.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(c => c.NameNe).HasMaxLength(100);
        builder.Property(c => c.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.CallingCode).HasMaxLength(8).IsRequired();
        builder.HasOne<CountryZone>().WithMany().HasForeignKey(c => c.CountryZoneId).OnDelete(DeleteBehavior.SetNull);

        builder.HasData(CountrySeedData.Countries.Select(c => new Country
        {
            Id = GeographyIds.CountryId(c.IsoCode),
            IsoCode = c.IsoCode,
            NameEn = c.NameEn,
            NameNe = CountrySeedData.NameNe.GetValueOrDefault(c.IsoCode),
            DefaultCurrency = c.DefaultCurrency,
            CallingCode = c.CallingCode,
            IsServiced = false,
            CountryZoneId = null,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        }));
    }
}
```

In `src/Persistence/AppDbContext.cs`: add after the `DeliveryZones` DbSet (near line 64):
```csharp
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<CountryZone> CountryZones => Set<CountryZone>();
```
Add `using Courier.Geography.Domain;` is already present (Province etc. use it). Add after `ApplyConfiguration(new DeliveryZoneConfiguration())` (near line 142):
```csharp
        modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
        modelBuilder.ApplyConfiguration(new CountryConfiguration());
```
(`CountryZoneConfiguration` first since `Country.CountryZoneId` references it — EF builds the full model graph regardless of call order, but this keeps FK-dependency order readable, matching how `DeliveryZoneConfiguration` is applied after `AreaConfiguration`.)

- [ ] **Step 4: Generate the migration**

Run from `D:\courier`: `dotnet ef migrations add AddCountriesAndCountryZones --project src/Persistence --startup-project src/Api --output-dir Migrations`
Open the generated file and confirm it creates `CountryZones`, then `Countries` (FK `CountryZoneId` nullable, `ON DELETE SET NULL`), then inserts all `CountrySeedData.Countries.Length` rows via `InsertData`. Confirm the snapshot diff is limited to these two tables.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Geography.Tests --nologo -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Geography/Infrastructure src/Persistence tests/Geography.Tests/CountrySeedDataTests.cs
git commit -m "feat(geography): seed Country/CountryZone reference tables with an additive migration"
```

---

### Task 3: Application services

**Files:**
- Create: `src/Modules/Geography/Application/CountryDtos.cs`
- Create: `src/Modules/Geography/Application/CountryQueryService.cs`
- Create: `src/Modules/Geography/Application/CountryAdminService.cs`
- Create: `src/Modules/Geography/Application/CountryZoneService.cs`
- Test: `tests/Geography.Tests/CountryQueryServiceTests.cs`
- Test: `tests/Geography.Tests/CountryAdminServiceTests.cs`
- Test: `tests/Geography.Tests/CountryZoneServiceTests.cs`

**Interfaces:**
- Consumes: `Country`, `CountryZone` (Task 1), `CountryConfiguration`/`CountryZoneConfiguration` (Task 2), `Courier.SharedKernel.PagedResult<T>`, `PaginationDefaults`, `Courier.Audit.Application.IAuditLogger`/`AuditEntry`.
- Produces: `CountryDto(Guid Id, string IsoCode, string NameEn, string? NameNe, string DefaultCurrency, string CallingCode, bool IsServiced, Guid? CountryZoneId, string? CountryZoneName)`; `UpdateCountryRequest(bool IsServiced, Guid? CountryZoneId)`; `CountryZoneDto(Guid Id, string Name, string? Description)`; `CreateCountryZoneRequest(string Name, string? Description)`; `UpdateCountryZoneRequest(string Name, string? Description)`; `CountryValidationException`; `CountryZoneValidationException`. `CountryQueryService.ListServicedAsync(CancellationToken)`, `.ListForAdminAsync(string? search, int? page, int? pageSize, CancellationToken)`. `CountryAdminService.UpdateAsync(Guid countryId, UpdateCountryRequest, Guid actingUserId, CancellationToken)` returns `CountryDto?` (`null` if not found). `CountryZoneService.ListAsync(CancellationToken)`, `.CreateAsync(CreateCountryZoneRequest, Guid actingUserId, CancellationToken)`, `.UpdateAsync(Guid zoneId, UpdateCountryZoneRequest, Guid actingUserId, CancellationToken)` returns `CountryZoneDto?`.

- [ ] **Step 1: Write the failing tests**

`tests/Geography.Tests/CountryQueryServiceTests.cs`:
```csharp
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class CountryQueryServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
            modelBuilder.ApplyConfiguration(new CountryConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task ListServicedAsync_ExcludesUnservicedCountries()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);

        var serviced = await service.ListServicedAsync();
        Assert.Empty(serviced);

        var nepal = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "NP");
        nepal.IsServiced = true;
        await dbContext.SaveChangesAsync();

        serviced = await service.ListServicedAsync();
        Assert.Equal("NP", Assert.Single(serviced).IsoCode);
    }

    [Fact]
    public async Task ListForAdminAsync_IncludesUnservicedRows_AndPaginates()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);
        var total = await dbContext.Set<Country>().CountAsync();

        var page1 = await service.ListForAdminAsync(search: null, page: 1, pageSize: 10);

        Assert.Equal(total, page1.TotalCount);
        Assert.Equal(10, page1.Items.Count);
    }

    [Fact]
    public async Task ListForAdminAsync_SearchMatchesIsoCodeOrName_CaseInsensitive()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryQueryService(dbContext);

        var byCode = await service.ListForAdminAsync(search: "np", page: 1, pageSize: 10);
        Assert.Equal("NP", Assert.Single(byCode.Items).IsoCode);

        var byName = await service.ListForAdminAsync(search: "nepal", page: 1, pageSize: 10);
        Assert.Equal("NP", Assert.Single(byName.Items).IsoCode);
    }

    [Fact]
    public async Task ListForAdminAsync_IncludesZoneName_WhenAssigned()
    {
        await using var dbContext = CreateSeededContext();
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = "SAARC", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<CountryZone>().Add(zone);
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        india.CountryZoneId = zone.Id;
        await dbContext.SaveChangesAsync();
        var service = new CountryQueryService(dbContext);

        var result = await service.ListForAdminAsync(search: "IN", page: 1, pageSize: 10);

        Assert.Equal("SAARC", Assert.Single(result.Items).CountryZoneName);
    }
}
```

`tests/Geography.Tests/CountryAdminServiceTests.cs`:
```csharp
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class RecordingAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

public class CountryAdminServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
            modelBuilder.ApplyConfiguration(new CountryConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task UpdateAsync_TogglesIsServiced_AndAudits()
    {
        await using var dbContext = CreateSeededContext();
        var audit = new RecordingAuditLogger();
        var service = new CountryAdminService(dbContext, audit);
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        var actingUserId = Guid.NewGuid();

        var result = await service.UpdateAsync(india.Id, new UpdateCountryRequest(true, null), actingUserId);

        Assert.NotNull(result);
        Assert.True(result!.IsServiced);
        Assert.True((await dbContext.Set<Country>().SingleAsync(c => c.Id == india.Id)).IsServiced);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("country.updated", entry.Action);
        Assert.Null(entry.OrganizationId);
        Assert.True(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task UpdateAsync_AssignsZone_AndReturnsZoneName()
    {
        await using var dbContext = CreateSeededContext();
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = "SAARC", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<CountryZone>().Add(zone);
        await dbContext.SaveChangesAsync();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");

        var result = await service.UpdateAsync(india.Id, new UpdateCountryRequest(true, zone.Id), Guid.NewGuid());

        Assert.Equal("SAARC", result!.CountryZoneName);
    }

    [Fact]
    public async Task UpdateAsync_UnknownZoneId_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());
        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");

        await Assert.ThrowsAsync<CountryValidationException>(() =>
            service.UpdateAsync(india.Id, new UpdateCountryRequest(true, Guid.NewGuid()), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_UnknownCountryId_ReturnsNull()
    {
        await using var dbContext = CreateSeededContext();
        var service = new CountryAdminService(dbContext, new RecordingAuditLogger());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateCountryRequest(true, null), Guid.NewGuid());

        Assert.Null(result);
    }
}
```

`tests/Geography.Tests/CountryZoneServiceTests.cs`:
```csharp
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CountryZoneServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task CreateAsync_PersistsZone()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        var result = await service.CreateAsync(new CreateCountryZoneRequest("SAARC", "South Asia"), Guid.NewGuid());

        Assert.Equal("SAARC", result.Name);
        Assert.Equal(1, await dbContext.Set<CountryZone>().CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_BlankName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.CreateAsync(new CreateCountryZoneRequest("  ", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RenamesZone()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        var created = await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());

        var updated = await service.UpdateAsync(created.Id, new UpdateCountryZoneRequest("South Asia", "Renamed"), Guid.NewGuid());

        Assert.Equal("South Asia", updated!.Name);
        Assert.Equal("Renamed", updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateNameAgainstAnotherZone_Throws()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("SAARC", null), Guid.NewGuid());
        var second = await service.CreateAsync(new CreateCountryZoneRequest("Middle East", null), Guid.NewGuid());

        await Assert.ThrowsAsync<CountryZoneValidationException>(() =>
            service.UpdateAsync(second.Id, new UpdateCountryZoneRequest("SAARC", null), Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateCountryZoneRequest("X", null), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_OrdersByName()
    {
        await using var dbContext = CreateContext();
        var service = new CountryZoneService(dbContext, new NoopAuditLogger());
        await service.CreateAsync(new CreateCountryZoneRequest("Middle East", null), Guid.NewGuid());
        await service.CreateAsync(new CreateCountryZoneRequest("Asia-Pacific", null), Guid.NewGuid());

        var zones = await service.ListAsync();

        Assert.Equal(new[] { "Asia-Pacific", "Middle East" }, zones.Select(z => z.Name));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Geography.Tests --nologo -v q`
Expected: build errors (application types don't exist yet).

- [ ] **Step 3: Implement**

`src/Modules/Geography/Application/CountryDtos.cs`:
```csharp
namespace Courier.Geography.Application;

public sealed record CountryDto(Guid Id, string IsoCode, string NameEn, string? NameNe, string DefaultCurrency, string CallingCode, bool IsServiced, Guid? CountryZoneId, string? CountryZoneName);

public sealed record UpdateCountryRequest(bool IsServiced, Guid? CountryZoneId);

public sealed record CountryZoneDto(Guid Id, string Name, string? Description);

public sealed record CreateCountryZoneRequest(string Name, string? Description);

public sealed record UpdateCountryZoneRequest(string Name, string? Description);

public class CountryValidationException : Exception
{
    public CountryValidationException(string message) : base(message) { }
}

public class CountryZoneValidationException : Exception
{
    public CountryZoneValidationException(string message) : base(message) { }
}
```

`src/Modules/Geography/Application/CountryQueryService.cs`:
```csharp
using Courier.Geography.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryQueryService
{
    private readonly DbContext _dbContext;

    public CountryQueryService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Backs GET /api/v1/geography/countries — the address-form read, dual-auth gated. Only
    // serviced countries are offered; the full list (including unserviced rows) is admin-only.
    public async Task<List<CountryDto>> ListServicedAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Country>()
            .Where(c => c.IsServiced)
            .OrderBy(c => c.NameEn)
            .Select(c => new CountryDto(c.Id, c.IsoCode, c.NameEn, c.NameNe, c.DefaultCurrency, c.CallingCode, c.IsServiced, c.CountryZoneId, null))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<CountryDto>> ListForAdminAsync(string? search, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var query = _dbContext.Set<Country>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(c => c.IsoCode.ToLower().Contains(term) || c.NameEn.ToLower().Contains(term) || (c.NameNe != null && c.NameNe.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var countries = await query
            .OrderBy(c => c.NameEn)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var zoneIds = countries.Where(c => c.CountryZoneId.HasValue).Select(c => c.CountryZoneId!.Value).Distinct().ToList();
        var zoneNames = await _dbContext.Set<CountryZone>().Where(z => zoneIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);

        var items = countries
            .Select(c => new CountryDto(c.Id, c.IsoCode, c.NameEn, c.NameNe, c.DefaultCurrency, c.CallingCode, c.IsServiced, c.CountryZoneId,
                c.CountryZoneId.HasValue ? zoneNames.GetValueOrDefault(c.CountryZoneId.Value) : null))
            .ToList();
        return new PagedResult<CountryDto>(items, normalizedPage, normalizedPageSize, total);
    }
}
```

`src/Modules/Geography/Application/CountryAdminService.cs`:
```csharp
using Courier.Audit.Application;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryAdminService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public CountryAdminService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<CountryDto?> UpdateAsync(Guid countryId, UpdateCountryRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var country = await _dbContext.Set<Country>().FirstOrDefaultAsync(c => c.Id == countryId, cancellationToken);
        if (country is null)
        {
            return null;
        }

        if (request.CountryZoneId is { } zoneId && !await _dbContext.Set<CountryZone>().AnyAsync(z => z.Id == zoneId, cancellationToken))
        {
            throw new CountryValidationException("Country zone was not found.");
        }

        var before = $"isServiced={country.IsServiced},countryZoneId={country.CountryZoneId}";
        country.IsServiced = request.IsServiced;
        country.CountryZoneId = request.CountryZoneId;
        country.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var after = $"isServiced={country.IsServiced},countryZoneId={country.CountryZoneId}";

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country.updated", "Country", country.Id.ToString(), before, after, IsCrossTenantAction: true), cancellationToken);

        var zoneName = country.CountryZoneId.HasValue
            ? await _dbContext.Set<CountryZone>().Where(z => z.Id == country.CountryZoneId).Select(z => z.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new CountryDto(country.Id, country.IsoCode, country.NameEn, country.NameNe, country.DefaultCurrency, country.CallingCode, country.IsServiced, country.CountryZoneId, zoneName);
    }
}
```

`src/Modules/Geography/Application/CountryZoneService.cs`:
```csharp
using Courier.Audit.Application;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryZoneService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public CountryZoneService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<List<CountryZoneDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<CountryZone>().OrderBy(z => z.Name).Select(z => new CountryZoneDto(z.Id, z.Name, z.Description)).ToListAsync(cancellationToken);

    public async Task<CountryZoneDto> CreateAsync(CreateCountryZoneRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CountryZoneValidationException("Name is required.");
        }
        if (await _dbContext.Set<CountryZone>().AnyAsync(z => z.Name == name, cancellationToken))
        {
            throw new CountryZoneValidationException("A country zone with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = name, Description = request.Description, CreatedAt = now, UpdatedAt = now };
        _dbContext.Set<CountryZone>().Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country_zone.created", "CountryZone", zone.Id.ToString(), After: name, IsCrossTenantAction: true), cancellationToken);
        return new CountryZoneDto(zone.Id, zone.Name, zone.Description);
    }

    public async Task<CountryZoneDto?> UpdateAsync(Guid zoneId, UpdateCountryZoneRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var zone = await _dbContext.Set<CountryZone>().FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        if (zone is null)
        {
            return null;
        }
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CountryZoneValidationException("Name is required.");
        }
        if (await _dbContext.Set<CountryZone>().AnyAsync(z => z.Id != zoneId && z.Name == name, cancellationToken))
        {
            throw new CountryZoneValidationException("A country zone with this name already exists.");
        }

        var before = zone.Name;
        zone.Name = name;
        zone.Description = request.Description;
        zone.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country_zone.updated", "CountryZone", zone.Id.ToString(), before, name, IsCrossTenantAction: true), cancellationToken);
        return new CountryZoneDto(zone.Id, zone.Name, zone.Description);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Geography.Tests --nologo -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Geography/Application tests/Geography.Tests/CountryQueryServiceTests.cs tests/Geography.Tests/CountryAdminServiceTests.cs tests/Geography.Tests/CountryZoneServiceTests.cs
git commit -m "feat(geography): add Country/CountryZone application services"
```

---

### Task 4: API endpoints and registration

**Files:**
- Modify: `src/Modules/Geography/Presentation/GeographyEndpoints.cs`
- Create: `src/Modules/Geography/Presentation/PlatformCountriesEndpoints.cs`
- Create: `src/Modules/Geography/Presentation/PlatformCountryZonesEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `CountryQueryService`, `CountryAdminService`, `CountryZoneService` (Task 3); `HasGeographyReadAccessAsync` (existing, in `GeographyEndpoints.cs`); `SuperAdminEndpointExtensions.RequireSuperAdmin` (existing, `Courier.SharedKernel`).
- Produces: `GET /api/v1/geography/countries` (dual-auth, serviced-only list); `GET /api/v1/platform/countries?search=&page=&pageSize=` and `PATCH /api/v1/platform/countries/{id}` (SuperAdmin); `GET/POST /api/v1/platform/country-zones` and `PATCH /api/v1/platform/country-zones/{id}` (SuperAdmin).

- [ ] **Step 1: Implement**

In `GeographyEndpoints.cs`, add inside `MapGeographyEndpoints` after the `/areas` route (still before the closing brace of the method, and before `HasGeographyReadAccessAsync`):
```csharp
        group.MapGet("/countries", async (CountryQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListServicedAsync(cancellationToken));
        });
```
Add `using Courier.Geography.Application;` if not already present (it is, via `GeographyQueryService`).

`src/Modules/Geography/Presentation/PlatformCountriesEndpoints.cs`:
```csharp
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class PlatformCountriesEndpoints
{
    public static void MapPlatformCountriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/countries").WithTags("Platform Countries");

        group.MapGet("/", async (string? search, int? page, int? pageSize, CountryQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListForAdminAsync(search, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapPatch("/{id:guid}", async (Guid id, UpdateCountryRequest request, CountryAdminService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.UpdateAsync(id, request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (CountryValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
```

`src/Modules/Geography/Presentation/PlatformCountryZonesEndpoints.cs`:
```csharp
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class PlatformCountryZonesEndpoints
{
    public static void MapPlatformCountryZonesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/country-zones").WithTags("Platform Country Zones");

        group.MapGet("/", async (CountryZoneService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (CreateCountryZoneRequest request, CountryZoneService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CreateAsync(request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Created($"/api/v1/platform/country-zones/{result.Id}", result);
            }
            catch (CountryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPatch("/{id:guid}", async (Guid id, UpdateCountryZoneRequest request, CountryZoneService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.UpdateAsync(id, request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (CountryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
```

In `src/Api/Program.cs`: add after `builder.Services.AddScoped<DeliveryZoneService>();` (near line 244):
```csharp
builder.Services.AddScoped<CountryQueryService>();
builder.Services.AddScoped<CountryAdminService>();
builder.Services.AddScoped<CountryZoneService>();
```
Add after `app.MapFxRateEndpoints();` (near line 439):
```csharp
app.MapPlatformCountriesEndpoints();
app.MapPlatformCountryZonesEndpoints();
```

- [ ] **Step 2: Build**

Run: `dotnet build Courier.slnx -v q --nologo`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Geography/Presentation src/Api/Program.cs
git commit -m "feat(geography): expose Country/CountryZone endpoints"
```

---

### Task 5: API tests

**Files:**
- Create: `tests/Api.Tests/CountriesGeographyEndpointTests.cs`
- Create: `tests/Api.Tests/PlatformCountriesEndpointsTests.cs`

**Interfaces:**
- Consumes: `RegisterOrgAsync`/`SuperAdminClientAsync`/`TenantOwnerClientAsync` patterns from `GeographyEndpointsTests.cs` and `FxRateEndpointsTests.cs`; `CreateMerchantAsync` pattern from `GeographyEndpointsTests.cs`; `GeographyIds.CountryId`.

Infra must be up (see Global Constraints) before running.

- [ ] **Step 1: Write the tests**

`tests/Api.Tests/CountriesGeographyEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class CountriesGeographyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public CountriesGeographyEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Country Org {suffix}", $"country-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient)
    {
        var ownerEmail = $"country-owner-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Country Traders Pvt. Ltd.", "Country Traders", "01-4444444", "contact@country.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return client;
    }

    [Fact]
    public async Task GetCountries_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/geography/countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Country rows are platform-level singletons shared by the whole test suite and are never
    // reset between runs against the persistent courier-postgres-1 container (unlike an
    // Organization, which each test creates fresh) — so this must NOT assert anything about a
    // country's state *before* this test's own PATCH (a prior run may have already toggled it).
    // Only idempotent, order-independent assertions belong here.
    [Fact]
    public async Task GetCountries_StaffToken_ReturnsOnlyServicedCountries()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var superAdmin = await SuperAdminClientAsync();
        var maldivesId = GeographyIds.CountryId("MV");
        var patch = await superAdmin.PatchAsJsonAsync($"/api/v1/platform/countries/{maldivesId}", new { isServiced = true, countryZoneId = (Guid?)null });
        patch.EnsureSuccessStatusCode();

        var countries = await (await client.GetAsync("/api/v1/geography/countries")).Content.ReadFromJsonAsync<List<CountryDto>>();
        Assert.Contains(countries!, c => c.IsoCode == "MV");
        Assert.All(countries!, c => Assert.True(c.IsServiced));
    }

    [Fact]
    public async Task GetCountries_MerchantOwnerToken_Succeeds()
    {
        var staffClient = _factory.CreateClient();
        var staffAuth = await RegisterOrgAsync(staffClient);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth.AccessToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var response = await merchantClient.GetAsync("/api/v1/geography/countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

`tests/Api.Tests/PlatformCountriesEndpointsTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformCountriesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformCountriesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return client;
    }

    private async Task<HttpClient> TenantOwnerClientAsync()
    {
        var client = _factory.CreateClient();
        var suffix = $"pc-{Guid.NewGuid():N}"[..16];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Country Org {suffix}", $"pc-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        return client;
    }

    [Fact]
    public async Task ListCountries_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform/countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListCountries_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();

        var response = await client.GetAsync("/api/v1/platform/countries");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCountry_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();
        var indiaId = GeographyIds.CountryId("IN");

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{indiaId}", new { isServiced = true, countryZoneId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListCountries_AsSuperAdmin_PaginatesAndSearches()
    {
        var client = await SuperAdminClientAsync();

        var page1 = await client.GetAsync("/api/v1/platform/countries?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
        var page1Body = await page1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, page1Body.GetProperty("items").GetArrayLength());

        var search = await client.GetAsync("/api/v1/platform/countries?search=nepal");
        var searchBody = await search.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, searchBody.GetProperty("items").GetArrayLength());
        Assert.Equal("NP", searchBody.GetProperty("items")[0].GetProperty("isoCode").GetString());
    }

    [Fact]
    public async Task UpdateCountry_AsSuperAdmin_TogglesServicedAndAssignsZone()
    {
        var client = await SuperAdminClientAsync();
        var zoneName = $"Zone {Guid.NewGuid():N}"[..12];
        var zoneCreate = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null });
        zoneCreate.EnsureSuccessStatusCode();
        var zone = await zoneCreate.Content.ReadFromJsonAsync<JsonElement>();
        var zoneId = zone.GetProperty("id").GetGuid();
        var qatarId = GeographyIds.CountryId("QA");

        var patch = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{qatarId}", new { isServiced = true, countryZoneId = zoneId });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isServiced").GetBoolean());
        Assert.Equal(zoneName, body.GetProperty("countryZoneName").GetString());
    }

    [Fact]
    public async Task UpdateCountry_UnknownZone_ReturnsBadRequestWithErrorContract()
    {
        var client = await SuperAdminClientAsync();
        var qatarId = GeographyIds.CountryId("QA");

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{qatarId}", new { isServiced = true, countryZoneId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COUNTRY_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCountry_UnknownId_ReturnsNotFound()
    {
        var client = await SuperAdminClientAsync();

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{Guid.NewGuid()}", new { isServiced = true, countryZoneId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCountryZone_DuplicateName_ReturnsBadRequestWithErrorContract()
    {
        var client = await SuperAdminClientAsync();
        var zoneName = $"Zone {Guid.NewGuid():N}"[..12];
        (await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COUNTRY_ZONE_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateCountryZone_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = "X", description = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Api.Tests --nologo -v q --filter "FullyQualifiedName~CountriesGeographyEndpointTests|FullyQualifiedName~PlatformCountriesEndpointsTests"`
Expected: PASS. This run also applies the new migration to the live DB.

- [ ] **Step 3: Verify seed row count in Postgres**

Run: `docker exec courier-postgres-1 psql -U courier -d courier -c "select count(*) from \"Countries\""`
Expected: matches `CountrySeedData.Countries.Length` (217, unless Task 2's array was adjusted).

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/CountriesGeographyEndpointTests.cs tests/Api.Tests/PlatformCountriesEndpointsTests.cs
git commit -m "test(api): cover country/country-zone endpoints, gating and validation"
```

---

### Task 6: Frontend data layer

**Files:**
- Create: `src/web/src/features/platform-countries/types.ts`
- Create: `src/web/src/features/platform-countries/hooks/usePlatformCountries.ts`
- Create: `src/web/src/features/platform-countries/hooks/usePlatformCountries.test.tsx`
- Create: `src/web/src/features/platform-countries/hooks/useUpdateCountry.ts`
- Create: `src/web/src/features/platform-countries/hooks/usePlatformCountryZones.ts`
- Create: `src/web/src/features/platform-countries/hooks/useCreateCountryZone.ts`
- Create: `src/web/src/features/platform-countries/hooks/useUpdateCountryZone.ts`
- Create: `src/web/src/app/api/platform/countries/route.ts`
- Create: `src/web/src/app/api/platform/countries/[id]/route.ts`
- Create: `src/web/src/app/api/platform/country-zones/route.ts`
- Create: `src/web/src/app/api/platform/country-zones/[id]/route.ts`

**Interfaces:**
- Produces: `Country { id, isoCode, nameEn, nameNe: string | null, defaultCurrency, callingCode, isServiced, countryZoneId: string | null, countryZoneName: string | null }`; `CountryZone { id, name, description: string | null }`. `usePlatformCountries(search, page, pageSize)`; `useUpdateCountry()` mutation input `{ id, isServiced, countryZoneId }`; `usePlatformCountryZones()`; `useCreateCountryZone()` input `{ name, description }`; `useUpdateCountryZone()` input `{ id, name, description }`.

- [ ] **Step 1: Write the failing hook test**

`src/web/src/features/platform-countries/hooks/usePlatformCountries.test.tsx`:
```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformCountries } from "./usePlatformCountries";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("usePlatformCountries", () => {
  it("builds the query string from page/pageSize with no search", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformCountries("", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/countries?page=1&pageSize=25", undefined);
  });

  it("includes search in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformCountries("nepal", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/countries?page=1&pageSize=25&search=nepal", undefined);
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run (from `src/web`): `npx vitest run src/features/platform-countries`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement**

`types.ts`:
```ts
export interface Country {
  id: string;
  isoCode: string;
  nameEn: string;
  nameNe: string | null;
  defaultCurrency: string;
  callingCode: string;
  isServiced: boolean;
  countryZoneId: string | null;
  countryZoneName: string | null;
}

export interface CountryZone {
  id: string;
  name: string;
  description: string | null;
}
```

`hooks/usePlatformCountries.ts`:
```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Country } from "@/features/platform-countries/types";

export function usePlatformCountries(search: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }

  return useQuery({
    queryKey: ["platform-countries", "list", search, page, pageSize],
    queryFn: () => clientFetch<PagedResult<Country>>(`/api/platform/countries?${params.toString()}`),
  });
}
```

`hooks/useUpdateCountry.ts`:
```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Country } from "@/features/platform-countries/types";

export function useUpdateCountry() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isServiced, countryZoneId }: { id: string; isServiced: boolean; countryZoneId: string | null }) =>
      clientFetch<Country>(`/api/platform/countries/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ isServiced, countryZoneId }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-countries"] }),
  });
}
```

`hooks/usePlatformCountryZones.ts`:
```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function usePlatformCountryZones() {
  return useQuery({
    queryKey: ["platform-country-zones"],
    queryFn: () => clientFetch<CountryZone[]>("/api/platform/country-zones"),
  });
}
```

`hooks/useCreateCountryZone.ts`:
```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function useCreateCountryZone() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; description?: string }) =>
      clientFetch<CountryZone>("/api/platform/country-zones", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-country-zones"] });
      queryClient.invalidateQueries({ queryKey: ["platform-countries"] });
    },
  });
}
```

`hooks/useUpdateCountryZone.ts`:
```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function useUpdateCountryZone() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: { id: string; name: string; description?: string }) =>
      clientFetch<CountryZone>(`/api/platform/country-zones/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-country-zones"] });
      queryClient.invalidateQueries({ queryKey: ["platform-countries"] });
    },
  });
}
```

`src/app/api/platform/countries/route.ts`:
```ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await platformApiFetch(`/api/v1/platform/countries${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/platform/countries/[id]/route.ts`:
```ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function PATCH(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch(`/api/v1/platform/countries/${params.id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/platform/country-zones/route.ts`:
```ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/country-zones");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch("/api/v1/platform/country-zones", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/platform/country-zones/[id]/route.ts`:
```ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function PATCH(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch(`/api/v1/platform/country-zones/${params.id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run**

Run (from `src/web`): `npx vitest run src/features/platform-countries`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/platform-countries src/web/src/app/api/platform/countries src/web/src/app/api/platform/country-zones
git commit -m "feat(web): add platform countries/country-zones data layer"
```

---

### Task 7: Frontend UI — countries admin page, zone sheet, nav link

**Files:**
- Create: `src/web/src/features/platform-countries/schema.ts`
- Create: `src/web/src/features/platform-countries/store/countryZonePanelStore.ts`
- Create: `src/web/src/features/platform-countries/store/countryZonePanelStore.test.ts`
- Create: `src/web/src/features/platform-countries/CountryZoneSheet.tsx`
- Create: `src/web/src/app/platform/countries/page.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.test.tsx`

**Interfaces:**
- Consumes: everything from Task 6.
- Produces: `countryZoneSchema`/`CountryZoneFormValues`; `useCountryZonePanelStore` with `panel: { mode: "closed" } | { mode: "create" } | { mode: "edit"; zoneId: string }`, `openCreate()`, `openEdit(zoneId)`, `close()`.

- [ ] **Step 1: Write the store test first**

`store/countryZonePanelStore.test.ts`:
```ts
import { describe, expect, it, beforeEach } from "vitest";
import { useCountryZonePanelStore } from "./countryZonePanelStore";

describe("countryZonePanelStore", () => {
  beforeEach(() => {
    useCountryZonePanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate opens create mode", () => {
    useCountryZonePanelStore.getState().openCreate();
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openEdit opens edit mode with the zone id", () => {
    useCountryZonePanelStore.getState().openEdit("zone-1");
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "edit", zoneId: "zone-1" });
  });

  it("close returns to closed", () => {
    useCountryZonePanelStore.getState().openCreate();
    useCountryZonePanelStore.getState().close();
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

Run (from `src/web`): `npx vitest run src/features/platform-countries/store`
Expected: FAIL (module not found).

- [ ] **Step 2: Implement store and schema**

`schema.ts`:
```ts
import { z } from "zod";

export const countryZoneSchema = z.object({
  name: z.string().min(1, "Name is required"),
  description: z.string().optional(),
});

export type CountryZoneFormValues = z.infer<typeof countryZoneSchema>;
```

`store/countryZonePanelStore.ts`:
```ts
import { create } from "zustand";

type CountryZonePanel = { mode: "closed" } | { mode: "create" } | { mode: "edit"; zoneId: string };

interface CountryZonePanelStore {
  panel: CountryZonePanel;
  openCreate: () => void;
  openEdit: (zoneId: string) => void;
  close: () => void;
}

// Global so the countries page's "New Zone" button and each zone row's edit action can open
// the same Sheet without routing to a separate page (CLAUDE.md convention: detail/create UI is
// always a right-side Sheet, never its own route). Country itself has no Sheet — its two mutable
// fields (isServiced, countryZoneId) are edited inline in the admin table row.
export const useCountryZonePanelStore = create<CountryZonePanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openEdit: (zoneId) => set({ panel: { mode: "edit", zoneId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Run the store test again. Expected: PASS.

- [ ] **Step 3: Implement `CountryZoneSheet.tsx`**

```tsx
"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { countryZoneSchema, type CountryZoneFormValues } from "@/features/platform-countries/schema";
import { usePlatformCountryZones } from "@/features/platform-countries/hooks/usePlatformCountryZones";
import { useCreateCountryZone } from "@/features/platform-countries/hooks/useCreateCountryZone";
import { useUpdateCountryZone } from "@/features/platform-countries/hooks/useUpdateCountryZone";
import { useCountryZonePanelStore } from "@/features/platform-countries/store/countryZonePanelStore";

export function CountryZoneSheet() {
  const panel = useCountryZonePanelStore((s) => s.panel);
  const close = useCountryZonePanelStore((s) => s.close);
  const isOpen = panel.mode !== "closed";
  const { data: zones } = usePlatformCountryZones();
  const create = useCreateCountryZone();
  const update = useUpdateCountryZone();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<CountryZoneFormValues>({
    resolver: zodResolver(countryZoneSchema),
    defaultValues: { name: "", description: "" },
  });

  useEffect(() => {
    if (panel.mode === "edit") {
      const existing = zones?.find((z) => z.id === panel.zoneId);
      reset({ name: existing?.name ?? "", description: existing?.description ?? "" });
    } else if (panel.mode === "create") {
      reset({ name: "", description: "" });
    }
  }, [panel, zones, reset]);

  async function onSubmit(values: CountryZoneFormValues) {
    setServerError(null);
    try {
      if (panel.mode === "edit") {
        await update.mutateAsync({ id: panel.zoneId, ...values });
      } else {
        await create.mutateAsync(values);
      }
      close();
    } catch {
      setServerError("Couldn't save the country zone. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{panel.mode === "edit" ? "Edit Country Zone" : "New Country Zone"}</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="zone-name">Name</Label>
            <Input id="zone-name" aria-invalid={!!errors.name} aria-describedby={errors.name ? "zone-name-error" : undefined} {...register("name")} />
            {errors.name && <FieldError id="zone-name-error">{errors.name.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="zone-description">Description</Label>
            <Textarea id="zone-description" rows={3} {...register("description")} />
          </div>
          {serverError && <FieldError id="zone-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Zone"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Implement the countries page**

`src/app/platform/countries/page.tsx`:
```tsx
"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformCountries } from "@/features/platform-countries/hooks/usePlatformCountries";
import { useUpdateCountry } from "@/features/platform-countries/hooks/useUpdateCountry";
import { usePlatformCountryZones } from "@/features/platform-countries/hooks/usePlatformCountryZones";
import { useCountryZonePanelStore } from "@/features/platform-countries/store/countryZonePanelStore";
import { CountryZoneSheet } from "@/features/platform-countries/CountryZoneSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { Country } from "@/features/platform-countries/types";

const PAGE_SIZE = 25;
const NO_ZONE = "none";

export default function PlatformCountriesPage() {
  const [searchInput, setSearchInput] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformCountries(debouncedSearch, page, PAGE_SIZE);
  const { data: zones } = usePlatformCountryZones();
  const updateCountry = useUpdateCountry();
  const openCreateZone = useCountryZonePanelStore((s) => s.openCreate);
  const openEditZone = useCountryZonePanelStore((s) => s.openEdit);

  const columns: ColumnDef<Country>[] = [
    { key: "isoCode", header: "Code", render: (c) => c.isoCode },
    { key: "name", header: "Name", render: (c) => c.nameNe ?? c.nameEn },
    { key: "currency", header: "Currency", render: (c) => c.defaultCurrency },
    {
      key: "zone",
      header: "Zone",
      render: (c) => (
        <Select
          value={c.countryZoneId ?? NO_ZONE}
          onValueChange={(value) => updateCountry.mutate({ id: c.id, isServiced: c.isServiced, countryZoneId: value === NO_ZONE ? null : value })}
        >
          <SelectTrigger className="w-40" aria-label={`Zone for ${c.nameEn}`}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={NO_ZONE}>No zone</SelectItem>
            {zones?.map((z) => (
              <SelectItem key={z.id} value={z.id}>{z.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      ),
    },
    {
      key: "serviced",
      header: "Serviced",
      render: (c) => (
        <Switch
          checked={c.isServiced}
          aria-label={`Toggle serviced for ${c.nameEn}`}
          onCheckedChange={(checked) => updateCountry.mutate({ id: c.id, isServiced: checked, countryZoneId: c.countryZoneId })}
        />
      ),
    },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load countries"
        description="Something went wrong fetching the country list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Countries</h1>
      </div>
      <Input
        value={searchInput}
        onChange={(e) => {
          setSearchInput(e.target.value);
          setPage(1);
        }}
        placeholder="Search by code or name"
        className="max-w-sm"
        aria-label="Search countries"
      />
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No countries found"
        emptyDescription="Try a different search term."
        onPageChange={setPage}
        rowKey={(c) => c.id}
      />

      <div className="flex items-center justify-between border-t pt-6">
        <h2 className="font-display text-lg font-semibold">Country Zones</h2>
        <Button size="sm" className="gap-1.5" onClick={() => openCreateZone()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          New Zone
        </Button>
      </div>
      <ul className="space-y-2">
        {zones?.map((zone) => (
          <li key={zone.id} className="flex items-center justify-between rounded-md border p-3 text-sm">
            <div>
              <div className="font-medium">{zone.name}</div>
              {zone.description && <div className="text-muted-foreground">{zone.description}</div>}
            </div>
            <Button size="sm" variant="ghost" onClick={() => openEditZone(zone.id)}>
              Edit
            </Button>
          </li>
        ))}
        {zones?.length === 0 && <li className="text-sm text-muted-foreground">No zones yet.</li>}
      </ul>

      <CountryZoneSheet />
    </div>
  );
}
```

- [ ] **Step 5: Add the nav link**

In `PlatformNav.tsx`, add to `links` after `{ href: "/platform/plans", label: "Plans" }`:
```tsx
  { href: "/platform/countries", label: "Countries" },
```

In `PlatformNav.test.tsx`, update the first test:
```tsx
  it("renders all eight platform nav links", () => {
    render(<PlatformNav />);

    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(screen.getByText("Organizations")).toBeInTheDocument();
    expect(screen.getByText("Plans")).toBeInTheDocument();
    expect(screen.getByText("Countries")).toBeInTheDocument();
    expect(screen.getByText("Invoices")).toBeInTheDocument();
    expect(screen.getByText("Audit Logs")).toBeInTheDocument();
    expect(screen.getByText("Jobs")).toBeInTheDocument();
    expect(screen.getByText("Queues")).toBeInTheDocument();
  });
```

- [ ] **Step 6: Run full frontend verification**

From `src/web`:
```
npx vitest run
npx tsc --noEmit
npm run lint
npm run build
```
Expected: all green apart from the pre-existing known-failing tsc errors noted in prior plans (`summary/route.test.ts` and marketing tests).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/platform-countries src/web/src/app/platform/countries src/web/src/features/platform/PlatformNav.tsx src/web/src/features/platform/PlatformNav.test.tsx
git commit -m "feat(web): add platform Countries admin page and country-zone management"
```

---

### Task 8: Docs and full verification

**Files:**
- Modify: `docs/INTERNATIONAL.md`
- Modify: `docs/DOMAIN-MODEL.md`
- Modify: `docs/ROADMAP.md`

- [ ] **Step 1: `docs/INTERNATIONAL.md`**

Change the status table's I2 row to: `| I2 | Countries, country zones, international address | Implemented — seeds Nepal + UN member states + a handful of dependent territories (not the complete ISO-3166-1 list); every country starts unserviced; Nepali names filled in for a 12-country starter set |`.

Add a new section after "## Notification locale (I1b)":
```markdown
## Countries and country zones (I2)

- **Data.** `Country` (`Geography` module, platform-level, no `OrganizationId`) is seeded with
  Nepal, every UN member state, Vatican City, Palestine, Kosovo, Taiwan, and a set of dependent
  territories relevant to international shipping (Hong Kong, Macao, Puerto Rico, and others — see
  `CountrySeedData`). Every row starts `IsServiced = false`; a SuperAdmin turns destinations on
  individually at `/platform/countries`. There is no create/delete API — the row set is fixed to
  the seed; more territories are added later via an additive migration if needed.
- **Names.** `NameEn` is authoritative for every row. `NameNe` is filled in only for a 12-country
  starter set (India, China, USA, UK, UAE, Qatar, Australia, Canada, Malaysia, South Korea, Saudi
  Arabia, Japan); every other row falls back to `NameEn` in the UI.
- **Country zones.** `CountryZone` ships empty; a SuperAdmin creates zones and assigns countries to
  them at `/platform/countries`. No pricing or reporting consumes zones yet (I5).
- **API.** `GET /api/v1/geography/countries` (serviced-only, dual-auth: staff `geography.read` or
  merchant `merchant.geography.read`) is the read any address form will use from I3 onward.
  `GET`/`PATCH /api/v1/platform/countries` and `GET`/`POST`/`PATCH /api/v1/platform/country-zones`
  are SuperAdmin-only — no new permission constants were needed.
- **`InternationalAddress`.** A format-validated value object (`Geography.Domain`) with no
  consumer yet; I3 will embed it in the shipment recipient/sender when `Scope = International`.

### Known limitations

- The seed is not the complete ISO-3166-1 alpha-2 list — uninhabited/unshippable territories
  (Antarctica, Bouvet Island, etc.) are excluded, and some inhabited dependent territories may be
  missing. There is no way to add a missing country without a migration.
- `InternationalAddress.CountryCode` is format-validated only; nothing yet checks it against a
  real `Country` row or `IsServiced`.
```

- [ ] **Step 2: `docs/DOMAIN-MODEL.md`**

In the "Money, FX and locale (international support, I0)" section, add a line after the `NotificationTemplate.Locale` bullet:
```markdown
- `Country` (Geography, platform-level, I2): ISO-3166 alpha-2 reference data, `IsServiced` toggle,
  optional `CountryZone`. `InternationalAddress` (Geography, I2): format-validated foreign address
  value object, no consumer yet.
```

- [ ] **Step 3: `docs/ROADMAP.md`**

In the "International Support" section, change `I2–I8 are planned.` to:
```markdown
I2 (Country/CountryZone reference data, serviced-country toggle, `InternationalAddress` value
object) is implemented; I3–I8 are planned.
```

- [ ] **Step 4: Full backend verification**

```bash
dotnet build Courier.slnx -v q --nologo
for p in SharedKernel Geography Audit Api; do dotnet test tests/$p.Tests --no-build --nologo -v q; done
```
Expected: 0 build errors; every project passes.

- [ ] **Step 5: Full frontend verification**

From `src/web`: `npx vitest run && npx tsc --noEmit && npm run lint && npm run build` (already run in Task 7 Step 6 — re-run only if Task 8's doc changes touched no `.ts`/`.tsx`, which they don't, so this step can be skipped if Task 7's run is still current).

- [ ] **Step 6: Commit**

```bash
git add docs
git commit -m "docs: document international I2 (countries, country zones, international address)"
```
