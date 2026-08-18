# Phase 3 — Nepal Geography, Zones, Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `docs/ROADMAP.md` Phase 3 — a `Geography` module (`Province, District,
LocalLevel, Ward, Area, DeliveryZone`) exists with real Nepal reference data seeded, `Branch`
replaces its free-text geography placeholder with FKs into this module, and delivery-zone
coverage lookups work off normalized data.

**Architecture:** A new `Geography` module (`src/Modules/Geography/Geography.csproj`) owns
`Province/District/LocalLevel/Ward/Area` (shared reference data, no `OrganizationId`, no tenant
query filter — seeded once, identical across every tenant) and `DeliveryZone` (org-owned, tenant
query filter like `Branch`). The dependency runs one direction only — `Branches → Geography` —
because `Branches` needs `Geography`'s hierarchy to validate a new branch's location; `Geography`
has no reciprocal need for anything in `Branches` or `Identity`. This mirrors the existing
`Identity → Branches` precedent from Phase 2 exactly. `Geography` exposes
`IGeographyLookupService` (Application-layer interface) that `Branches.Application.BranchService`
consumes to validate a branch's `ProvinceId/DistrictId/LocalLevelId/WardNumber` chain
server-side before persisting — never trusted blindly from client input (CLAUDE.md §33). The one
place `DeliveryZone.OrganizationId` needs an FK to `Organization` is
`Persistence/AppDbContext.OnModelCreating`, for the same reason `Branch.OrganizationId`'s FK
lives there: `Geography` has no project reference to `Identity`.

Per `CLARIFICATIONS.md` #2, `Branch.WardNumber` is a plain `int` scoped to `LocalLevelId`, **not**
a FK to `Ward.Id` — identical to `MerchantAddress`'s field shape in `DOMAIN-MODEL.md`. The
normalized `Ward` entity (with its own `Id`) exists so `Area`/`DeliveryZone` coverage can FK into
it; a `Branch`'s own address only needs to say "ward number 7 of this local level", which
`IGeographyLookupService.IsValidHierarchyAsync` still validates against real seeded `Ward` rows.

**Tech Stack:** Same as Phase 1/2 — EF Core 10 + Npgsql, xUnit + `Microsoft.EntityFrameworkCore.InMemory`
for pure unit tests (mirrors `tests/Branches.Tests`), `Microsoft.AspNetCore.Mvc.Testing` for
API-level integration tests against the real Postgres container from `docker-compose.yml`.

**Spec:** `docs/ROADMAP.md` (Phase 3), `docs/DOMAIN-MODEL.md` (Geography, Branches sections),
`docs/CLARIFICATIONS.md` #2, `docs/ARCHITECTURE.md` §2–3, `docs/RBAC.md` §3 (permission catalog).

## Global Constraints

- `Province/District/LocalLevel/Ward/Area` carry **no** `OrganizationId` and get **no** EF Core
  tenant query filter — they are shared reference data, identical for every tenant on the
  platform, seeded once via `HasData`.
- `DeliveryZone` **is** tenant-owned: `OrganizationId` + the same fail-closed
  `HasQueryFilter(z => _currentTenantProvider.OrganizationId != null && z.OrganizationId == ...)`
  pattern Phase 1/2 established on `Branch`/`UserBranch`.
- `Branch.WardNumber` is a plain `int`, not a `Ward` FK (see Architecture above). Do not add a
  `Branch.WardId` — that would contradict `CLARIFICATIONS.md` #2.
- `Geography` module has no project reference to `Identity` or `Branches`. The dependency runs
  `Branches → Geography` only, mirroring `Identity → Branches` from Phase 2. If a future task
  seems to need the reverse, that is a signal the design needs to change, not a signal to add the
  reference.
- Seed data scope (approved 2026-08-18): all 7 real provinces and all 77 real districts via
  `HasData`. `LocalLevel/Ward/Area` seeded only for the 5 districts CLAUDE.md §1's example org
  already operates in (Kathmandu, Lalitpur, Bhaktapur, Kaski, Rupandehi) — one representative
  urban local level per district, wards 1–3, two areas per ward. Full 753-local-level national
  coverage is an explicitly out-of-scope follow-up data-loading task, not part of this plan.
- Deterministic seed IDs: mirror `Courier.Identity.Application.PermissionCatalog.IdFor` exactly
  (MD5 hash of a stable string key → `Guid`) so `HasData` rows are stable across regenerated
  migrations. Do not use `Guid.NewGuid()` for seeded rows.
- New permissions added to `PermissionCatalog.All`: `geography.read` (org-wide, ungated by branch
  — reference-data lookups), `zone.read`, `zone.create`, `zone.update` (org-owned `DeliveryZone`
  CRUD, same shape as `branch.read/create/update`). No `if (user.Role == "Admin")` anywhere —
  CLAUDE.md §6.
- `Branch` creation/update must reject any `ProvinceId/DistrictId/LocalLevelId/WardNumber`
  combination that does not nest correctly — validated server-side via
  `IGeographyLookupService.IsValidHierarchyAsync`, never trusted from the client as independently
  correct fields (CLAUDE.md §33).
- Commit after every task using conventional prefixes (`feat:`, `chore:`, `refactor:`, `test:`).
- Every integration test in this plan that touches `AppDbContext` requires the shared
  `courier-postgres-1` container (`docker exec courier-postgres-1 pg_isready -U courier`) — do
  **not** `docker compose up` a second Postgres from a worktree; reuse the existing one on port
  5434, per this project's established local-Postgres convention.

---

### Task 1: Scaffold Geography Module and Wire Project References

**Files:**
- Create: `src/Modules/Geography/Geography.csproj`
- Create: `tests/Geography.Tests/Geography.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Modules/Branches/Branches.csproj` (add reference to Geography)
- Modify: `src/Persistence/Persistence.csproj` (add reference to Geography)
- Modify: `src/Api/Api.csproj` (add reference to Geography)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Geography` project wired into the solution with the reference
  graph `SharedKernel, Audit ← Geography`, `SharedKernel, Audit, Geography ← Branches`,
  `..., Geography ← Persistence`, `..., Geography ← Api`. No cycles — `Geography` does not
  reference `Branches` or `Identity`. `Api.Tests` gets transitive access to `Geography` types
  through its existing `Api.csproj` reference (SDK-style `ProjectReference` is transitive), so it
  needs no direct reference added. Later tasks add real code.

- [ ] **Step 1: Create the two projects**

```bash
dotnet new classlib -n Geography -o src/Modules/Geography
dotnet new xunit -n Geography.Tests -o tests/Geography.Tests
dotnet sln Courier.slnx add src/Modules/Geography/Geography.csproj tests/Geography.Tests/Geography.Tests.csproj
```

Delete the template `src/Modules/Geography/Class1.cs` and `tests/Geography.Tests/UnitTest1.cs` —
both projects start empty.

- [ ] **Step 2: Add the ASP.NET Core framework reference to Geography.csproj**

`Geography.Presentation` (added in a later task) needs `IEndpointRouteBuilder`,
`RouteHandlerBuilder`, `HttpContext`. Add to `src/Modules/Geography/Geography.csproj`, inside a
sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 3: Add EF Core packages**

```bash
dotnet add src/Modules/Geography/Geography.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Geography/Geography.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Geography.Tests/Geography.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 4: Wire project references**

```bash
dotnet add src/Modules/Geography/Geography.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Geography/Geography.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Branches/Branches.csproj reference src/Modules/Geography/Geography.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Geography/Geography.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Geography/Geography.csproj
dotnet add tests/Geography.Tests/Geography.Tests.csproj reference src/Modules/Geography/Geography.csproj
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add Courier.slnx src/Modules/Geography tests/Geography.Tests src/Modules/Branches/Branches.csproj src/Persistence/Persistence.csproj src/Api/Api.csproj
git commit -m "chore: scaffold Geography module project and wire references"
```

---

### Task 2: Geography Domain, EF Configuration, and Seed Data

**Files:**
- Create: `src/Modules/Geography/Domain/Province.cs`
- Create: `src/Modules/Geography/Domain/District.cs`
- Create: `src/Modules/Geography/Domain/LocalLevelType.cs`
- Create: `src/Modules/Geography/Domain/LocalLevel.cs`
- Create: `src/Modules/Geography/Domain/Ward.cs`
- Create: `src/Modules/Geography/Domain/Area.cs`
- Create: `src/Modules/Geography/Domain/DeliveryZone.cs`
- Create: `src/Modules/Geography/Infrastructure/GeographyIds.cs`
- Create: `src/Modules/Geography/Infrastructure/GeographySeedData.cs`
- Create: `src/Modules/Geography/Infrastructure/ProvinceConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/DistrictConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/LocalLevelConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/WardConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/AreaConfiguration.cs`
- Create: `src/Modules/Geography/Infrastructure/DeliveryZoneConfiguration.cs`
- Test: `tests/Geography.Tests/GeographySeedDataTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Province, District, LocalLevel(Type), Ward, Area, DeliveryZone(CoveredAreas)` domain
  types; `GeographyIds.{ProvinceId,DistrictId,LocalLevelId,WardId,AreaId}(...)` deterministic-Guid
  helpers; `GeographySeedData.{Provinces,Districts,LocalLevels,WardNumbers,AreaLabels}` seed
  source data. Task 3's `GeographyQueryService` and Task 7's `Branch` FKs both consume these
  configurations' seeded rows; Task 6 wires the `DbSet`s into `AppDbContext`.

- [ ] **Step 1: Write the domain entities**

`src/Modules/Geography/Domain/Province.cs`:

```csharp
namespace Courier.Geography.Domain;

public class Province
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}
```

`src/Modules/Geography/Domain/District.cs`:

```csharp
namespace Courier.Geography.Domain;

public class District
{
    public Guid Id { get; set; }
    public Guid ProvinceId { get; set; }
    public required string Name { get; set; }
}
```

`src/Modules/Geography/Domain/LocalLevelType.cs`:

```csharp
namespace Courier.Geography.Domain;

public enum LocalLevelType
{
    Metropolitan,
    SubMetropolitan,
    Municipality,
    RuralMunicipality
}
```

`src/Modules/Geography/Domain/LocalLevel.cs`:

```csharp
namespace Courier.Geography.Domain;

public class LocalLevel
{
    public Guid Id { get; set; }
    public Guid DistrictId { get; set; }
    public required string Name { get; set; }
    public LocalLevelType Type { get; set; }
}
```

`src/Modules/Geography/Domain/Ward.cs`:

```csharp
namespace Courier.Geography.Domain;

public class Ward
{
    public Guid Id { get; set; }
    public Guid LocalLevelId { get; set; }
    public int Number { get; set; }
}
```

`src/Modules/Geography/Domain/Area.cs`:

```csharp
namespace Courier.Geography.Domain;

public class Area
{
    public Guid Id { get; set; }
    public Guid WardId { get; set; }
    public required string Name { get; set; }
}
```

`src/Modules/Geography/Domain/DeliveryZone.cs`:

```csharp
namespace Courier.Geography.Domain;

public class DeliveryZone
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }

    // Coverage as a navigation (skip-navigation many-to-many via DeliveryZoneAreas), mirroring
    // Role.Permissions — DOMAIN-MODEL.md's "CoveredAreaIds[]" is the DTO-level projection of
    // this, not a raw scalar array column.
    public ICollection<Area> CoveredAreas { get; set; } = new List<Area>();
}
```

- [ ] **Step 2: Write the deterministic-ID helper**

`src/Modules/Geography/Infrastructure/GeographyIds.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Courier.Geography.Infrastructure;

// Deterministic Guid generation for seeded reference rows, mirroring
// Courier.Identity.Application.PermissionCatalog.IdFor — the same key always produces the same
// Guid, so HasData rows stay stable across regenerated EF migrations.
public static class GeographyIds
{
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
```

- [ ] **Step 3: Write the seed source data**

`src/Modules/Geography/Infrastructure/GeographySeedData.cs`:

```csharp
using Courier.Geography.Domain;

namespace Courier.Geography.Infrastructure;

// Real Nepal geography reference data, scoped per this phase's approved seed plan (2026-08-18):
// full provinces/districts (stable, small, genuinely useful everywhere), partial local
// levels/wards/areas limited to the 5 districts CLAUDE.md §1's example org operates in. Full
// 753-local-level national coverage is a follow-up data-loading task, not application logic.
public static class GeographySeedData
{
    public static readonly string[] Provinces =
    {
        "Koshi", "Madhesh", "Bagmati", "Gandaki", "Lumbini", "Karnali", "Sudurpashchim"
    };

    // (Province, District) — all 77 districts, official as of the 2015 federal restructuring.
    public static readonly (string Province, string District)[] Districts =
    {
        ("Koshi", "Bhojpur"), ("Koshi", "Dhankuta"), ("Koshi", "Ilam"), ("Koshi", "Jhapa"),
        ("Koshi", "Khotang"), ("Koshi", "Morang"), ("Koshi", "Okhaldhunga"), ("Koshi", "Panchthar"),
        ("Koshi", "Sankhuwasabha"), ("Koshi", "Solukhumbu"), ("Koshi", "Sunsari"), ("Koshi", "Taplejung"),
        ("Koshi", "Terhathum"), ("Koshi", "Udayapur"),

        ("Madhesh", "Bara"), ("Madhesh", "Dhanusha"), ("Madhesh", "Mahottari"), ("Madhesh", "Parsa"),
        ("Madhesh", "Rautahat"), ("Madhesh", "Saptari"), ("Madhesh", "Sarlahi"), ("Madhesh", "Siraha"),

        ("Bagmati", "Bhaktapur"), ("Bagmati", "Chitwan"), ("Bagmati", "Dhading"), ("Bagmati", "Dolakha"),
        ("Bagmati", "Kathmandu"), ("Bagmati", "Kavrepalanchok"), ("Bagmati", "Lalitpur"), ("Bagmati", "Makwanpur"),
        ("Bagmati", "Nuwakot"), ("Bagmati", "Ramechhap"), ("Bagmati", "Rasuwa"), ("Bagmati", "Sindhuli"),
        ("Bagmati", "Sindhupalchok"),

        ("Gandaki", "Baglung"), ("Gandaki", "Gorkha"), ("Gandaki", "Kaski"), ("Gandaki", "Lamjung"),
        ("Gandaki", "Manang"), ("Gandaki", "Mustang"), ("Gandaki", "Myagdi"), ("Gandaki", "Nawalpur"),
        ("Gandaki", "Parbat"), ("Gandaki", "Syangja"), ("Gandaki", "Tanahun"),

        ("Lumbini", "Arghakhanchi"), ("Lumbini", "Banke"), ("Lumbini", "Bardiya"), ("Lumbini", "Dang"),
        ("Lumbini", "Eastern Rukum"), ("Lumbini", "Gulmi"), ("Lumbini", "Kapilvastu"), ("Lumbini", "Parasi"),
        ("Lumbini", "Palpa"), ("Lumbini", "Pyuthan"), ("Lumbini", "Rolpa"), ("Lumbini", "Rupandehi"),

        ("Karnali", "Dailekh"), ("Karnali", "Dolpa"), ("Karnali", "Humla"), ("Karnali", "Jajarkot"),
        ("Karnali", "Jumla"), ("Karnali", "Kalikot"), ("Karnali", "Mugu"), ("Karnali", "Salyan"),
        ("Karnali", "Surkhet"), ("Karnali", "Western Rukum"),

        ("Sudurpashchim", "Achham"), ("Sudurpashchim", "Baitadi"), ("Sudurpashchim", "Bajhang"),
        ("Sudurpashchim", "Bajura"), ("Sudurpashchim", "Dadeldhura"), ("Sudurpashchim", "Darchula"),
        ("Sudurpashchim", "Doti"), ("Sudurpashchim", "Kailali"), ("Sudurpashchim", "Kanchanpur"),
    };

    // (Province, District, LocalLevel, Type) — one representative urban local level per district,
    // for the 5 districts CLAUDE.md §1's example org (Head Office/Kathmandu Hub/Lalitpur
    // Branch/Bhaktapur Branch/Pokhara Hub/Butwal Branch) actually operates in.
    public static readonly (string Province, string District, string LocalLevel, LocalLevelType Type)[] LocalLevels =
    {
        ("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", LocalLevelType.Metropolitan),
        ("Bagmati", "Lalitpur", "Lalitpur Metropolitan City", LocalLevelType.Metropolitan),
        ("Bagmati", "Bhaktapur", "Bhaktapur Municipality", LocalLevelType.Municipality),
        ("Gandaki", "Kaski", "Pokhara Metropolitan City", LocalLevelType.Metropolitan),
        ("Lumbini", "Rupandehi", "Butwal Sub-Metropolitan City", LocalLevelType.SubMetropolitan),
    };

    // Ward numbers 1-3 seeded for each of the 5 local levels above (every one of them genuinely
    // has at least wards 1-3, so this is real data, just not the full ward count).
    public static readonly int[] WardNumbers = { 1, 2, 3 };

    // Area labels seeded per ward — representative test/demo coverage granularity, not claimed
    // as authoritative neighborhood-boundary data.
    public static readonly string[] AreaLabels = { "Area A", "Area B" };
}
```

- [ ] **Step 4: Write the EF Core configurations**

`src/Modules/Geography/Infrastructure/ProvinceConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("Provinces");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();

        builder.HasData(GeographySeedData.Provinces.Select(name => new Province
        {
            Id = GeographyIds.ProvinceId(name),
            Name = name
        }));
    }
}
```

`src/Modules/Geography/Infrastructure/DistrictConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => d.ProvinceId);

        builder.HasOne<Province>()
            .WithMany()
            .HasForeignKey(d => d.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(GeographySeedData.Districts.Select(d => new District
        {
            Id = GeographyIds.DistrictId(d.Province, d.District),
            ProvinceId = GeographyIds.ProvinceId(d.Province),
            Name = d.District
        }));
    }
}
```

`src/Modules/Geography/Infrastructure/LocalLevelConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class LocalLevelConfiguration : IEntityTypeConfiguration<LocalLevel>
{
    public void Configure(EntityTypeBuilder<LocalLevel> builder)
    {
        builder.ToTable("LocalLevels");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(l => l.DistrictId);

        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(l => l.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(GeographySeedData.LocalLevels.Select(l => new LocalLevel
        {
            Id = GeographyIds.LocalLevelId(l.Province, l.District, l.LocalLevel),
            DistrictId = GeographyIds.DistrictId(l.Province, l.District),
            Name = l.LocalLevel,
            Type = l.Type
        }));
    }
}
```

`src/Modules/Geography/Infrastructure/WardConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class WardConfiguration : IEntityTypeConfiguration<Ward>
{
    public void Configure(EntityTypeBuilder<Ward> builder)
    {
        builder.ToTable("Wards");
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => w.LocalLevelId);
        builder.HasIndex(w => new { w.LocalLevelId, w.Number }).IsUnique();

        builder.HasOne<LocalLevel>()
            .WithMany()
            .HasForeignKey(w => w.LocalLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        var wards = new List<Ward>();
        foreach (var l in GeographySeedData.LocalLevels)
        {
            var localLevelId = GeographyIds.LocalLevelId(l.Province, l.District, l.LocalLevel);
            foreach (var number in GeographySeedData.WardNumbers)
            {
                wards.Add(new Ward
                {
                    Id = GeographyIds.WardId(l.Province, l.District, l.LocalLevel, number),
                    LocalLevelId = localLevelId,
                    Number = number
                });
            }
        }
        builder.HasData(wards);
    }
}
```

`src/Modules/Geography/Infrastructure/AreaConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("Areas");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(a => a.WardId);

        builder.HasOne<Ward>()
            .WithMany()
            .HasForeignKey(a => a.WardId)
            .OnDelete(DeleteBehavior.Restrict);

        var areas = new List<Area>();
        foreach (var l in GeographySeedData.LocalLevels)
        {
            foreach (var number in GeographySeedData.WardNumbers)
            {
                var wardId = GeographyIds.WardId(l.Province, l.District, l.LocalLevel, number);
                foreach (var label in GeographySeedData.AreaLabels)
                {
                    areas.Add(new Area
                    {
                        Id = GeographyIds.AreaId(l.Province, l.District, l.LocalLevel, number, label),
                        WardId = wardId,
                        Name = $"{l.LocalLevel} Ward {number} {label}"
                    });
                }
            }
        }
        builder.HasData(areas);
    }
}
```

`src/Modules/Geography/Infrastructure/DeliveryZoneConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("DeliveryZones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(150).IsRequired();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(z => z.OrganizationId);

        builder.HasMany(z => z.CoveredAreas).WithMany()
            .UsingEntity(j => j.ToTable("DeliveryZoneAreas"));

        // No FK to Organizations here: Geography has no project reference to Identity, same
        // reasoning as Branch.OrganizationId (see Persistence/AppDbContext.OnModelCreating,
        // wired centrally there instead).
    }
}
```

- [ ] **Step 5: Write the failing seed-data test**

`tests/Geography.Tests/GeographySeedDataTests.cs`:

```csharp
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographySeedDataTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
            modelBuilder.ApplyConfiguration(new DistrictConfiguration());
            modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
            modelBuilder.ApplyConfiguration(new WardConfiguration());
            modelBuilder.ApplyConfiguration(new AreaConfiguration());
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
    public async Task Seed_ProducesExpectedRowCounts()
    {
        await using var dbContext = CreateSeededContext();

        Assert.Equal(7, await dbContext.Set<Province>().CountAsync());
        Assert.Equal(77, await dbContext.Set<District>().CountAsync());
        Assert.Equal(5, await dbContext.Set<LocalLevel>().CountAsync());
        Assert.Equal(15, await dbContext.Set<Ward>().CountAsync()); // 5 local levels x 3 wards
        Assert.Equal(30, await dbContext.Set<Area>().CountAsync()); // 15 wards x 2 areas
    }

    [Fact]
    public async Task Seed_KathmanduChain_NestsCorrectly()
    {
        await using var dbContext = CreateSeededContext();

        var province = await dbContext.Set<Province>().SingleAsync(p => p.Name == "Bagmati");
        var district = await dbContext.Set<District>().SingleAsync(d => d.Name == "Kathmandu");
        Assert.Equal(province.Id, district.ProvinceId);

        var localLevel = await dbContext.Set<LocalLevel>().SingleAsync(l => l.Name == "Kathmandu Metropolitan City");
        Assert.Equal(district.Id, localLevel.DistrictId);
        Assert.Equal(LocalLevelType.Metropolitan, localLevel.Type);

        var wards = await dbContext.Set<Ward>().Where(w => w.LocalLevelId == localLevel.Id).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, wards.Select(w => w.Number).OrderBy(n => n));

        var ward1 = wards.Single(w => w.Number == 1);
        var areas = await dbContext.Set<Area>().Where(a => a.WardId == ward1.Id).ToListAsync();
        Assert.Equal(2, areas.Count);
    }

    [Fact]
    public void GeographyIds_AreDeterministicAndUnique()
    {
        var allIds = new List<Guid>();
        foreach (var province in GeographySeedData.Provinces)
        {
            allIds.Add(GeographyIds.ProvinceId(province));
        }
        foreach (var (province, district) in GeographySeedData.Districts)
        {
            allIds.Add(GeographyIds.DistrictId(province, district));
        }
        foreach (var (province, district, localLevel, _) in GeographySeedData.LocalLevels)
        {
            allIds.Add(GeographyIds.LocalLevelId(province, district, localLevel));
        }

        Assert.Equal(allIds.Count, allIds.Distinct().Count());
        Assert.Equal(GeographyIds.ProvinceId("Bagmati"), GeographyIds.ProvinceId("Bagmati"));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj`
Expected: 3 tests pass (this is TDD-by-configuration: the configurations already exist from Step
4, so these tests validate the seed wiring rather than driving new production code — that is
correct here since `HasData` seed rows are inert until asserted against).

- [ ] **Step 7: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Geography tests/Geography.Tests
git commit -m "feat: add Geography domain entities, EF configurations, and Nepal seed data"
```

---

### Task 3: Geography Application — Read Lookups and IGeographyLookupService

**Files:**
- Create: `src/Modules/Geography/Application/GeographyDtos.cs`
- Create: `src/Modules/Geography/Application/IGeographyLookupService.cs`
- Create: `src/Modules/Geography/Infrastructure/GeographyQueryService.cs`
- Test: `tests/Geography.Tests/GeographyQueryServiceTests.cs`

**Interfaces:**
- Consumes: `Province/District/LocalLevel/Ward/Area` + configurations (Task 2).
- Produces: `IGeographyLookupService.IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid
  localLevelId, int wardNumber, CancellationToken)` — Task 7's `BranchService` consumes this
  exact signature. `GeographyQueryService.{ListProvincesAsync,ListDistrictsAsync,
  ListLocalLevelsAsync,ListWardsAsync,ListAreasAsync}` — Task 5's `GeographyEndpoints` consumes
  these exact method names/return types.

- [ ] **Step 1: Write the response DTOs**

`src/Modules/Geography/Application/GeographyDtos.cs`:

```csharp
using Courier.Geography.Domain;

namespace Courier.Geography.Application;

public record ProvinceResponse(Guid Id, string Name);

public record DistrictResponse(Guid Id, Guid ProvinceId, string Name);

public record LocalLevelResponse(Guid Id, Guid DistrictId, string Name, LocalLevelType Type);

public record WardResponse(Guid Id, Guid LocalLevelId, int Number);

public record AreaResponse(Guid Id, Guid WardId, string Name);
```

- [ ] **Step 2: Write the lookup interface**

`src/Modules/Geography/Application/IGeographyLookupService.cs`:

```csharp
namespace Courier.Geography.Application;

public interface IGeographyLookupService
{
    // True iff ward `wardNumber` actually exists under `localLevelId`, `localLevelId` actually
    // exists under `districtId`, and `districtId` actually exists under `provinceId` — i.e. the
    // full chain is a real, correctly nested Nepal geography path, not just four independently
    // valid ids.
    Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing test**

`tests/Geography.Tests/GeographyQueryServiceTests.cs`:

```csharp
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographyQueryServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
            modelBuilder.ApplyConfiguration(new DistrictConfiguration());
            modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
            modelBuilder.ApplyConfiguration(new WardConfiguration());
            modelBuilder.ApplyConfiguration(new AreaConfiguration());
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
    public async Task ListProvincesAsync_ReturnsAllSevenOrderedByName()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var provinces = await service.ListProvincesAsync();

        Assert.Equal(7, provinces.Count);
        Assert.Contains(provinces, p => p.Name == "Bagmati");
        Assert.Equal(provinces.OrderBy(p => p.Name).Select(p => p.Name), provinces.Select(p => p.Name));
    }

    [Fact]
    public async Task ListDistrictsAsync_FiltersByProvince()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var bagmatiId = GeographyIds.ProvinceId("Bagmati");

        var districts = await service.ListDistrictsAsync(bagmatiId);

        Assert.Equal(13, districts.Count);
        Assert.Contains(districts, d => d.Name == "Kathmandu");
        Assert.All(districts, d => Assert.Equal(bagmatiId, d.ProvinceId));
    }

    [Fact]
    public async Task ListLocalLevelsAsync_FiltersByDistrict()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var kathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");

        var localLevels = await service.ListLocalLevelsAsync(kathmanduDistrictId);

        var localLevel = Assert.Single(localLevels);
        Assert.Equal("Kathmandu Metropolitan City", localLevel.Name);
        Assert.Equal(LocalLevelType.Metropolitan, localLevel.Type);
    }

    [Fact]
    public async Task ListWardsAsync_ReturnsThreeWardsOrderedByNumber()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var kathmanduMetroId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

        var wards = await service.ListWardsAsync(kathmanduMetroId);

        Assert.Equal(new[] { 1, 2, 3 }, wards.Select(w => w.Number));
    }

    [Fact]
    public async Task ListAreasAsync_ReturnsTwoAreasForWard()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);
        var ward1Id = GeographyIds.WardId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1);

        var areas = await service.ListAreasAsync(ward1Id);

        Assert.Equal(2, areas.Count);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_CorrectChain_ReturnsTrue()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber: 2);

        Assert.True(result);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_LocalLevelFromDifferentDistrict_ReturnsFalse()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        // Kathmandu's province/district paired with Pokhara's local level — mismatched chain.
        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City"),
            wardNumber: 1);

        Assert.False(result);
    }

    [Fact]
    public async Task IsValidHierarchyAsync_WardNumberNotSeeded_ReturnsFalse()
    {
        await using var dbContext = CreateSeededContext();
        var service = new GeographyQueryService(dbContext);

        var result = await service.IsValidHierarchyAsync(
            GeographyIds.ProvinceId("Bagmati"),
            GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber: 99);

        Assert.False(result);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj`
Expected: FAIL with "GeographyQueryService could not be found" (type does not exist yet).

- [ ] **Step 5: Implement GeographyQueryService**

`src/Modules/Geography/Infrastructure/GeographyQueryService.cs`:

```csharp
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Infrastructure;

public class GeographyQueryService : IGeographyLookupService
{
    private readonly DbContext _dbContext;

    public GeographyQueryService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Province>()
            .OrderBy(p => p.Name)
            .Select(p => new ProvinceResponse(p.Id, p.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<DistrictResponse>> ListDistrictsAsync(Guid provinceId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<District>()
            .Where(d => d.ProvinceId == provinceId)
            .OrderBy(d => d.Name)
            .Select(d => new DistrictResponse(d.Id, d.ProvinceId, d.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<LocalLevelResponse>> ListLocalLevelsAsync(Guid districtId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<LocalLevel>()
            .Where(l => l.DistrictId == districtId)
            .OrderBy(l => l.Name)
            .Select(l => new LocalLevelResponse(l.Id, l.DistrictId, l.Name, l.Type))
            .ToListAsync(cancellationToken);

    public async Task<List<WardResponse>> ListWardsAsync(Guid localLevelId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Ward>()
            .Where(w => w.LocalLevelId == localLevelId)
            .OrderBy(w => w.Number)
            .Select(w => new WardResponse(w.Id, w.LocalLevelId, w.Number))
            .ToListAsync(cancellationToken);

    public async Task<List<AreaResponse>> ListAreasAsync(Guid wardId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Area>()
            .Where(a => a.WardId == wardId)
            .OrderBy(a => a.Name)
            .Select(a => new AreaResponse(a.Id, a.WardId, a.Name))
            .ToListAsync(cancellationToken);

    public async Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Ward>()
            .Where(w => w.Id != Guid.Empty && w.LocalLevelId == localLevelId && w.Number == wardNumber)
            .Join(_dbContext.Set<LocalLevel>(), w => w.LocalLevelId, l => l.Id, (w, l) => l)
            .Where(l => l.DistrictId == districtId)
            .Join(_dbContext.Set<District>(), l => l.DistrictId, d => d.Id, (l, d) => d)
            .AnyAsync(d => d.ProvinceId == provinceId, cancellationToken);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj`
Expected: all 8 tests pass.

- [ ] **Step 7: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Geography/Application src/Modules/Geography/Infrastructure/GeographyQueryService.cs tests/Geography.Tests/GeographyQueryServiceTests.cs
git commit -m "feat: add Geography read lookups and IGeographyLookupService"
```

---

### Task 4: Geography Application — DeliveryZone CRUD

**Files:**
- Create: `src/Modules/Geography/Application/DeliveryZoneDtos.cs`
- Create: `src/Modules/Geography/Application/DeliveryZoneService.cs`
- Test: `tests/Geography.Tests/DeliveryZoneServiceTests.cs`

**Interfaces:**
- Consumes: `Area`/`AreaConfiguration` etc. (Task 2), `DeliveryZone`/`DeliveryZoneConfiguration`
  (Task 2).
- Produces: `DeliveryZoneService.{CreateAsync,GetAsync,ListAsync,UpdateAsync}` — Task 5's
  `DeliveryZonesEndpoints` consumes these exact method names/signatures.

- [ ] **Step 1: Write the DTOs and validation exception**

`src/Modules/Geography/Application/DeliveryZoneDtos.cs`:

```csharp
namespace Courier.Geography.Application;

public record CreateDeliveryZoneRequest(string Name, List<Guid> CoveredAreaIds);

public record UpdateDeliveryZoneRequest(string Name, List<Guid> CoveredAreaIds);

public record DeliveryZoneResponse(Guid Id, string Name, IReadOnlyList<Guid> CoveredAreaIds);

public class DeliveryZoneValidationException : Exception
{
    public DeliveryZoneValidationException(string message) : base(message) { }
}
```

- [ ] **Step 2: Write the failing test**

`tests/Geography.Tests/DeliveryZoneServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class DeliveryZoneServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
            modelBuilder.ApplyConfiguration(new DistrictConfiguration());
            modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
            modelBuilder.ApplyConfiguration(new WardConfiguration());
            modelBuilder.ApplyConfiguration(new AreaConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static List<Guid> KathmanduWard1Areas() =>
        new()
        {
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
        };

    [Fact]
    public async Task CreateAsync_ValidAreas_PersistsZoneWithCoverage()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());

        var result = await service.CreateAsync(
            new CreateDeliveryZoneRequest("Kathmandu Ward 1", KathmanduWard1Areas()),
            ActingUser(Guid.NewGuid()));

        Assert.Equal("Kathmandu Ward 1", result.Name);
        Assert.Equal(2, result.CoveredAreaIds.Count);
    }

    [Fact]
    public async Task CreateAsync_UnknownAreaId_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());

        await Assert.ThrowsAsync<DeliveryZoneValidationException>(() =>
            service.CreateAsync(
                new CreateDeliveryZoneRequest("Bad Zone", new List<Guid> { Guid.NewGuid() }),
                ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateAsync_NoAreas_ThrowsValidationException()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());

        await Assert.ThrowsAsync<DeliveryZoneValidationException>(() =>
            service.CreateAsync(
                new CreateDeliveryZoneRequest("Empty Zone", new List<Guid>()),
                ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetAsync_ThenUpdateAsync_ChangesNameAndCoverage()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());
        var created = await service.CreateAsync(
            new CreateDeliveryZoneRequest("Original Name", KathmanduWard1Areas()),
            ActingUser(Guid.NewGuid()));

        var ward2Areas = new List<Guid>
        {
            GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 2, "Area A"),
        };
        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateDeliveryZoneRequest("Renamed", ward2Areas),
            ActingUser(Guid.NewGuid()));

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(ward2Areas, updated.CoveredAreaIds);
    }

    [Fact]
    public async Task ListAsync_ReturnsCreatedZone()
    {
        await using var dbContext = CreateSeededContext();
        var service = new DeliveryZoneService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger());
        await service.CreateAsync(new CreateDeliveryZoneRequest("Listed Zone", KathmanduWard1Areas()), ActingUser(Guid.NewGuid()));

        var zones = await service.ListAsync();

        Assert.Contains(zones, z => z.Name == "Listed Zone");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj`
Expected: FAIL with "DeliveryZoneService could not be found" (type does not exist yet).

- [ ] **Step 4: Implement DeliveryZoneService**

`src/Modules/Geography/Application/DeliveryZoneService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class DeliveryZoneService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public DeliveryZoneService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<DeliveryZoneResponse> CreateAsync(CreateDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new DeliveryZoneValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new DeliveryZoneValidationException("Acting user could not be resolved.");

        var areas = await ValidateAndLoadAreasAsync(request.CoveredAreaIds, cancellationToken);

        var zone = new DeliveryZone
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            CoveredAreas = areas
        };
        _dbContext.Set<DeliveryZone>().Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone.created", "DeliveryZone", zone.Id.ToString()), cancellationToken);

        return ToResponse(zone);
    }

    public async Task<DeliveryZoneResponse?> GetAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var zone = await _dbContext.Set<DeliveryZone>()
            .Include(z => z.CoveredAreas)
            .FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        return zone is null ? null : ToResponse(zone);
    }

    public async Task<List<DeliveryZoneResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var zones = await _dbContext.Set<DeliveryZone>().Include(z => z.CoveredAreas).ToListAsync(cancellationToken);
        return zones.Select(ToResponse).ToList();
    }

    public async Task<DeliveryZoneResponse?> UpdateAsync(Guid zoneId, UpdateDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new DeliveryZoneValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new DeliveryZoneValidationException("Acting user could not be resolved.");

        var zone = await _dbContext.Set<DeliveryZone>()
            .Include(z => z.CoveredAreas)
            .FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        var before = string.Join(",", zone.CoveredAreas.Select(a => a.Id));
        var areas = await ValidateAndLoadAreasAsync(request.CoveredAreaIds, cancellationToken);

        zone.Name = request.Name;
        zone.CoveredAreas.Clear();
        foreach (var area in areas)
        {
            zone.CoveredAreas.Add(area);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = string.Join(",", zone.CoveredAreas.Select(a => a.Id));
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone.updated", "DeliveryZone", zone.Id.ToString(), before, after), cancellationToken);

        return ToResponse(zone);
    }

    private async Task<List<Area>> ValidateAndLoadAreasAsync(IReadOnlyList<Guid> areaIds, CancellationToken cancellationToken)
    {
        if (areaIds.Count == 0)
        {
            throw new DeliveryZoneValidationException("A delivery zone must cover at least one area.");
        }

        var distinctIds = areaIds.Distinct().ToList();
        var areas = await _dbContext.Set<Area>().Where(a => distinctIds.Contains(a.Id)).ToListAsync(cancellationToken);
        if (areas.Count != distinctIds.Count)
        {
            throw new DeliveryZoneValidationException("One or more covered area ids were not found.");
        }
        return areas;
    }

    private static DeliveryZoneResponse ToResponse(DeliveryZone zone) =>
        new(zone.Id, zone.Name, zone.CoveredAreas.Select(a => a.Id).ToList());
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj`
Expected: all tests pass (13 total across the project so far).

- [ ] **Step 6: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Geography/Application/DeliveryZoneDtos.cs src/Modules/Geography/Application/DeliveryZoneService.cs tests/Geography.Tests/DeliveryZoneServiceTests.cs
git commit -m "feat: add DeliveryZone CRUD application service"
```

---

### Task 5: Geography Presentation — Endpoints, Permission Catalog, DI Wiring

**Files:**
- Create: `src/Modules/Geography/Presentation/GeographyEndpoints.cs`
- Create: `src/Modules/Geography/Presentation/DeliveryZonesEndpoints.cs`
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `GeographyQueryService` (Task 3), `DeliveryZoneService` (Task 4).
- Produces: `GET /api/v1/geography/{provinces,districts,local-levels,wards,areas}`,
  `POST/GET/PUT /api/v1/delivery-zones`, and the four new permission catalog entries every later
  task's authorization checks rely on.

- [ ] **Step 1: Add the four new permissions to the catalog**

In `src/Modules/Identity/Application/PermissionCatalog.cs`, after the existing
`("branch.update", "Update branches"),` line, add:

```csharp
        ("geography.read", "Read geography reference data"),
        ("zone.read", "Read delivery zones"),
        ("zone.create", "Create delivery zones"),
        ("zone.update", "Update delivery zones"),
```

(These are added to `PermissionCatalog.All`, which `AuthService`'s org-registration flow already
grants in full to the registering owner's role — no additional role-seeding wiring is needed for
new entries to take effect.)

- [ ] **Step 2: Write the geography read-lookup endpoints**

`src/Modules/Geography/Presentation/GeographyEndpoints.cs`:

```csharp
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class GeographyEndpoints
{
    public static void MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/geography");

        group.MapGet("/provinces", async (GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListProvincesAsync(cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/districts", async (Guid provinceId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDistrictsAsync(provinceId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/local-levels", async (Guid districtId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListLocalLevelsAsync(districtId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/wards", async (Guid localLevelId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListWardsAsync(localLevelId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/areas", async (Guid wardId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAreasAsync(wardId, cancellationToken))).RequirePermission("geography.read");
    }
}
```

- [ ] **Step 3: Write the delivery-zone CRUD endpoints**

`src/Modules/Geography/Presentation/DeliveryZonesEndpoints.cs`:

```csharp
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class DeliveryZonesEndpoints
{
    public static void MapDeliveryZonesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/delivery-zones");

        group.MapPost("/", async (CreateDeliveryZoneRequest request, DeliveryZoneService zoneService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await zoneService.CreateAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/delivery-zones/{result.Id}", result);
            }
            catch (DeliveryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "DELIVERY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("zone.create");

        group.MapGet("/", async (DeliveryZoneService zoneService, CancellationToken cancellationToken) =>
            Results.Ok(await zoneService.ListAsync(cancellationToken))).RequirePermission("zone.read");

        group.MapGet("/{id:guid}", async (Guid id, DeliveryZoneService zoneService, CancellationToken cancellationToken) =>
        {
            var zone = await zoneService.GetAsync(id, cancellationToken);
            return zone is null ? Results.NotFound() : Results.Ok(zone);
        }).RequirePermission("zone.read");

        group.MapPut("/{id:guid}", async (Guid id, UpdateDeliveryZoneRequest request, DeliveryZoneService zoneService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await zoneService.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (DeliveryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "DELIVERY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("zone.update");
    }
}
```

- [ ] **Step 4: Wire DI registrations and map the endpoints in Program.cs**

In `src/Api/Program.cs`, add to the `using` list:

```csharp
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Geography.Presentation;
```

After the existing `builder.Services.AddScoped<BranchService>();` line, add:

```csharp
builder.Services.AddScoped<GeographyQueryService>();
builder.Services.AddScoped<IGeographyLookupService>(sp => sp.GetRequiredService<GeographyQueryService>());
builder.Services.AddScoped<DeliveryZoneService>();
```

After the existing `app.MapBranchesEndpoints();` line, add:

```csharp
app.MapGeographyEndpoints();
app.MapDeliveryZonesEndpoints();
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Geography/Presentation src/Modules/Identity/Application/PermissionCatalog.cs src/Api/Program.cs
git commit -m "feat: add Geography endpoints, permissions, and DI wiring"
```

---

### Task 6: Persistence — Wire Geography into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: all Geography configurations (Task 2).
- Produces: `AppDbContext.{Provinces,Districts,LocalLevels,Wards,Areas,DeliveryZones}`, a
  migration creating all six tables plus seed data. Task 7's `Branch` FKs and Task 8's Api.Tests
  both query through this context.

- [ ] **Step 1: Update AppDbContext**

Replace the full contents of `src/Persistence/AppDbContext.cs`:

```csharp
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenantProvider)
        : base(options)
    {
        _currentTenantProvider = currentTenantProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<LocalLevel> LocalLevels => Set<LocalLevel>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
        modelBuilder.ApplyConfiguration(new UserBranchConfiguration());
        modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
        modelBuilder.ApplyConfiguration(new DistrictConfiguration());
        modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
        modelBuilder.ApplyConfiguration(new WardConfiguration());
        modelBuilder.ApplyConfiguration(new AreaConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());

        // Branch.OrganizationId -> Organizations FK lives here rather than in
        // BranchConfiguration (Branches module) because Branches deliberately has no project
        // reference to Identity — the dependency runs the other way (Identity -> Branches, for
        // UserBranch's FK to Branch and IBranchHierarchyService). Persistence already
        // references both modules, so it's the one place that can wire this specific
        // constraint without introducing a project cycle.
        modelBuilder.Entity<Branch>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for DeliveryZone.OrganizationId: Geography has no project reference to
        // Identity.
        modelBuilder.Entity<DeliveryZone>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(z => z.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tenant filters fail CLOSED: the explicit `OrganizationId != null` guard stops EF Core's
        // null-semantics translation from degrading these into `WHERE "OrganizationId" IS NULL`
        // when no tenant is resolved, which would otherwise match org-less rows (planned for
        // future platform/SuperAdmin accounts) instead of matching nothing.
        modelBuilder.Entity<User>().HasQueryFilter(u => _currentTenantProvider.OrganizationId != null && u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => _currentTenantProvider.OrganizationId != null && ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Branch>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserBranch>().HasQueryFilter(ub => _currentTenantProvider.OrganizationId != null && ub.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryZone>().HasQueryFilter(z => _currentTenantProvider.OrganizationId != null && z.OrganizationId == _currentTenantProvider.OrganizationId);

        // Defensive: nothing queries Organizations with a tenant context today (registration only
        // inserts), but without this a future `Organizations.ToListAsync()` would enumerate every
        // tenant on the platform.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => _currentTenantProvider.OrganizationId != null && o.Id == _currentTenantProvider.OrganizationId);

        // No tenant filter on RefreshToken: its lookups happen before the caller's tenant is known
        // (see AuthService.RefreshTokenAsync), and rows are keyed by an unguessable SHA-256 token
        // hash rather than being enumerable. Filtering it would break the refresh flow outright.

        // No tenant filter on Province/District/LocalLevel/Ward/Area: shared reference data,
        // identical across every tenant, per ARCHITECTURE.md §2's Geography module description.
    }
}
```

- [ ] **Step 2: Ensure Postgres is running, then generate and apply the migration**

```bash
docker exec courier-postgres-1 pg_isready -U courier
```

If it's not already running (this project reuses the shared container across worktrees — do not
`docker compose up` a second one):

```bash
docker compose up -d postgres
```

Then:

```bash
dotnet ef migrations add AddGeography --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update`
reports it applied `AddGeography` with no errors.

- [ ] **Step 3: Verify the new tables and seed data exist**

```bash
docker exec -it courier-postgres-1 psql -U courier -d courier -c "SELECT COUNT(*) FROM \"Provinces\";"
docker exec -it courier-postgres-1 psql -U courier -d courier -c "SELECT COUNT(*) FROM \"Districts\";"
docker exec -it courier-postgres-1 psql -U courier -d courier -c "SELECT COUNT(*) FROM \"LocalLevels\";"
docker exec -it courier-postgres-1 psql -U courier -d courier -c "SELECT COUNT(*) FROM \"Wards\";"
docker exec -it courier-postgres-1 psql -U courier -d courier -c "SELECT COUNT(*) FROM \"Areas\";"
```

Expected: counts of 7, 77, 5, 15, 30 respectively.

- [ ] **Step 4: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Geography into AppDbContext with seeded migration"
```

---

### Task 7: Branches Integration — Geography FKs on Branch

**Files:**
- Modify: `src/Modules/Branches/Domain/Branch.cs`
- Modify: `src/Modules/Branches/Infrastructure/BranchConfiguration.cs`
- Modify: `src/Modules/Branches/Application/BranchDtos.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `tests/Branches.Tests/BranchHierarchyServiceTests.cs`
- Modify: `tests/Api.Tests/BranchesEndpointsTests.cs`
- Modify: `tests/Api.Tests/BranchAuthorizationEndpointTests.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: `IGeographyLookupService.IsValidHierarchyAsync` (Task 3), `GeographyIds` (Task 2).
- Produces: `Branch` with real geography FKs, no free-text geography remains —
  `CreateBranchRequest`/`BranchResponse` both gain `ProvinceId, DistrictId, LocalLevelId,
  WardNumber` in that position (right after `Type`, before `AddressLine`). This is the exit
  criterion from `ROADMAP.md` Phase 3.

- [ ] **Step 1: Update the Branch domain entity**

Replace the full contents of `src/Modules/Branches/Domain/Branch.cs`:

```csharp
namespace Courier.Branches.Domain;

public class Branch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public BranchType Type { get; set; }

    // Normalized Nepal geography (ROADMAP Phase 3, CLARIFICATIONS.md #2). WardNumber is a plain
    // int scoped to LocalLevelId, not a FK to Ward.Id — Ward exists as its own entity so
    // Area/DeliveryZone coverage can FK into it, but a Branch's own address only needs to name
    // "ward number N of this local level", identical to MerchantAddress's field shape.
    public required Guid ProvinceId { get; set; }
    public required Guid DistrictId { get; set; }
    public required Guid LocalLevelId { get; set; }
    public required int WardNumber { get; set; }

    public required string AddressLine { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Phone { get; set; }
    public string? OperatingHours { get; set; }
    public Guid? ParentBranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Update BranchConfiguration**

Replace the full contents of `src/Modules/Branches/Infrastructure/BranchConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Branches.Infrastructure;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Phone).HasMaxLength(30);
        builder.Property(b => b.OperatingHours).HasMaxLength(100);

        // Branch codes (e.g. "KTM-HUB") are unique per organization, not globally.
        builder.HasIndex(b => new { b.OrganizationId, b.Code }).IsUnique();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => b.ParentBranchId);
        builder.HasIndex(b => b.ProvinceId);
        builder.HasIndex(b => b.DistrictId);
        builder.HasIndex(b => b.LocalLevelId);

        // Self-referencing hierarchy FK. Restrict: a branch with children cannot be deleted
        // out from under them by a cascading delete.
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(b => b.ParentBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Geography FKs: unlike Organization (see below), Branches DOES have a project reference
        // to Geography (this plan's Task 1), so these are configured directly here rather than
        // centrally in AppDbContext.
        builder.HasOne<Province>()
            .WithMany()
            .HasForeignKey(b => b.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(b => b.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>()
            .WithMany()
            .HasForeignKey(b => b.LocalLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization here: Branches has no project reference to Identity (see this
        // plan's Architecture section). That FK is configured centrally in
        // Persistence/AppDbContext.OnModelCreating instead.
    }
}
```

- [ ] **Step 3: Update BranchDtos**

Replace the full contents of `src/Modules/Branches/Application/BranchDtos.cs`:

```csharp
using Courier.Branches.Domain;

namespace Courier.Branches.Application;

public record CreateBranchRequest(
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId);

public record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    bool IsActive);

public class BranchValidationException : Exception
{
    public BranchValidationException(string message) : base(message) { }
}
```

- [ ] **Step 4: Update BranchService to validate geography server-side**

Replace the full contents of `src/Modules/Branches/Application/BranchService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Application;

public class BranchService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IGeographyLookupService _geographyLookupService;

    public BranchService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IGeographyLookupService geographyLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _geographyLookupService = geographyLookupService;
    }

    public async Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new BranchValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new BranchValidationException("Acting user could not be resolved.");

        var codeTaken = await _dbContext.Set<Branch>()
            .AnyAsync(b => b.Code == request.Code, cancellationToken);
        if (codeTaken)
        {
            throw new BranchValidationException($"Branch code '{request.Code}' is already in use.");
        }

        var validGeography = await _geographyLookupService.IsValidHierarchyAsync(
            request.ProvinceId, request.DistrictId, request.LocalLevelId, request.WardNumber, cancellationToken);
        if (!validGeography)
        {
            throw new BranchValidationException("Province/District/LocalLevel/WardNumber do not form a valid geography chain.");
        }

        if (request.ParentBranchId is Guid parentId)
        {
            var parentExists = await _dbContext.Set<Branch>()
                .AnyAsync(b => b.Id == parentId, cancellationToken);
            if (!parentExists)
            {
                throw new BranchValidationException("Parent branch was not found in this organization.");
            }

            var accessibleForCreate = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "branch.create", cancellationToken);
            if (accessibleForCreate is not null && !accessibleForCreate.Contains(parentId))
            {
                throw new BranchValidationException("Parent branch is outside your branch access.");
            }
        }

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            ProvinceId = request.ProvinceId,
            DistrictId = request.DistrictId,
            LocalLevelId = request.LocalLevelId,
            WardNumber = request.WardNumber,
            AddressLine = request.AddressLine,
            Phone = request.Phone,
            OperatingHours = request.OperatingHours,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ParentBranchId = request.ParentBranchId,
            IsActive = true
        };
        _dbContext.Set<Branch>().Add(branch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "branch.created", "Branch", branch.Id.ToString()), cancellationToken);

        return ToResponse(branch);
    }

    public async Task<BranchResponse?> GetBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        return branch is null ? null : ToResponse(branch);
    }

    public async Task<List<BranchResponse>> ListBranchesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", cancellationToken);

        var query = _dbContext.Set<Branch>().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var ids = accessibleBranchIds.ToArray();
            query = query.Where(b => ids.Contains(b.Id));
        }

        var branches = await query.ToListAsync(cancellationToken);
        return branches.Select(ToResponse).ToList();
    }

    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.Type,
        branch.ProvinceId, branch.DistrictId, branch.LocalLevelId, branch.WardNumber,
        branch.AddressLine, branch.Phone, branch.OperatingHours, branch.Latitude, branch.Longitude,
        branch.ParentBranchId, branch.IsActive);
}
```

- [ ] **Step 5: Update BranchHierarchyServiceTests' NewBranch helper**

In `tests/Branches.Tests/BranchHierarchyServiceTests.cs`, replace:

```csharp
    private static Branch NewBranch(Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        AddressLine = "Somewhere",
        ParentBranchId = parentId
    };
```

with:

```csharp
    private static Branch NewBranch(Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        ProvinceId = Guid.NewGuid(),
        DistrictId = Guid.NewGuid(),
        LocalLevelId = Guid.NewGuid(),
        WardNumber = 1,
        AddressLine = "Somewhere",
        ParentBranchId = parentId
    };
```

(The EF Core InMemory provider does not enforce FK integrity, so arbitrary ids are fine here —
this test only exercises `BranchHierarchyService`'s parent/child walk, not geography validation.)

- [ ] **Step 6: Update BranchesEndpointsTests for the new required fields**

Replace the full contents of `tests/Api.Tests/BranchesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class BranchesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public BranchesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Branches Org {suffix}", $"branches-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task PostBranches_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "New Road, Kathmandu", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_OwnerWithAllBranchesScope_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "New Road, Kathmandu", "01-4444444", "09:00-18:00", 27.7m, 85.3m, null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(created);

        var getResponse = await client.GetAsync($"/api/v1/branches/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("KTM-HUB", fetched.Code);
    }

    [Fact]
    public async Task CreateBranch_DuplicateCode_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var request = new CreateBranchRequest(
            "DUP-01", "First", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null);
        await client.PostAsJsonAsync("/api/v1/branches", request);

        var response = await client.PostAsJsonAsync("/api/v1/branches", request with { Name = "Second" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBranch_MismatchedGeographyChain_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // Kathmandu's province/district paired with Pokhara's local level — mismatched chain.
        var pokharaLocalLevelId = GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City");
        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "BAD-GEO-01", "Bad Geo Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, pokharaLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("valid geography chain", body);
    }

    [Fact]
    public async Task GetBranch_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync($"/api/v1/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_ReturnsCreatedBranch()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LST-01", "List Test Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));

        var response = await client.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = await response.Content.ReadFromJsonAsync<List<BranchResponse>>();
        Assert.Contains(branches!, b => b.Code == "LST-01");
    }
}
```

- [ ] **Step 7: Update BranchAuthorizationEndpointTests for the new required fields**

Replace the full contents of `tests/Api.Tests/BranchAuthorizationEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class BranchAuthorizationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public BranchAuthorizationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Scoped Org {suffix}", $"scoped-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    // Same direct-seeding approach as GetBranch_ScopedUser_...: no grant/user-management endpoint
    // exists yet this phase, so the scoped test user's role, UserRole scope and UserBranch grants
    // are persisted straight against AppDbContext, exactly like a future "invite user" flow would.
    private async Task<string> SeedScopedUserTokenAsync(
        Guid organizationId, Guid ownerUserId, string permissionKey, params Guid[] grantedBranchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = $"ScopedRole-{permissionKey}",
            IsSystemDefault = false
        };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        var email = $"scoped-{scopedUserId:N}@example.com";
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped User"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = ownerUserId
        });
        foreach (var branchId in grantedBranchIds)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(),
                UserId = scopedUserId,
                BranchId = branchId,
                OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = ownerUserId
            });
        }
        await dbContext.SaveChangesAsync();

        return jwtTokenService.IssueAccessToken(scopedUserId, organizationId, email);
    }

    [Fact]
    public async Task ListBranches_ScopedUser_ReturnsOnlyGrantedBranches()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var grantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LSG-01", "Granted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        grantedResponse.EnsureSuccessStatusCode();
        var granted = (await grantedResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var ungrantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LSU-01", "Ungranted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Elsewhere", null, null, null, null, null));
        ungrantedResponse.EnsureSuccessStatusCode();

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "branch.read", granted.Id);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var listResponse = await scopedClient.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var branches = (await listResponse.Content.ReadFromJsonAsync<List<BranchResponse>>())!;
        Assert.Contains(branches, b => b.Code == "LSG-01");
        Assert.DoesNotContain(branches, b => b.Code == "LSU-01");
    }

    [Fact]
    public async Task CreateBranch_ScopedUser_OutOfScopeParentBranch_Returns400()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var grantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRG-01", "Granted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        grantedResponse.EnsureSuccessStatusCode();
        var granted = (await grantedResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var outOfScopeResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRO-01", "Out Of Scope Parent", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Elsewhere", null, null, null, null, null));
        outOfScopeResponse.EnsureSuccessStatusCode();
        var outOfScopeParent = (await outOfScopeResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "branch.create", granted.Id);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var response = await scopedClient.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRN-01", "Attempted Child", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Nowhere", null, null, null, null, outOfScopeParent.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Pin the reason: a 400 here must be the branch-scope rejection, not an unrelated
        // validation failure such as a duplicate branch code.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("outside your branch access", body);
    }

    [Fact]
    public async Task GetBranch_ScopedUser_ForbiddenWithoutGrant_ThenAllowedAfterParentGrant()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var parentResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "PAR-01", "Parent Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        var parent = await parentResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CHD-01", "Child Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere Else", null, null, null, null, parent!.Id));
        var child = await childResponse.Content.ReadFromJsonAsync<BranchResponse>();

        // No grant/user-management endpoint exists yet this phase, so the scoped test user's
        // role and UserBranch grant are seeded directly against AppDbContext, exactly like a
        // future "invite user" flow would persist them.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == "branch.read");
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = owner.OrganizationId, Name = "ScopedReader", IsSystemDefault = false };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = owner.OrganizationId,
            Email = $"scoped-{scopedUserId:N}@example.com",
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped Reader"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = owner.OrganizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var scopedToken = jwtTokenService.IssueAccessToken(scopedUserId, owner.OrganizationId, $"scoped-{scopedUserId:N}@example.com");
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var forbiddenResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            BranchId = parent.Id,
            OrganizationId = owner.OrganizationId,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var allowedResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }
}
```

- [ ] **Step 8: Generate and apply the migration**

```bash
docker exec courier-postgres-1 pg_isready -U courier
dotnet ef migrations add AddBranchGeographyFields --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update`
reports it applied `AddBranchGeographyFields` with no errors.

- [ ] **Step 9: Run the full test suite**

```bash
dotnet test Courier.slnx
```

Expected: all tests pass, including the rewritten `BranchesEndpointsTests` and
`BranchAuthorizationEndpointTests`.

- [ ] **Step 10: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Branches src/Persistence tests/Branches.Tests/BranchHierarchyServiceTests.cs tests/Api.Tests/BranchesEndpointsTests.cs tests/Api.Tests/BranchAuthorizationEndpointTests.cs
git commit -m "feat: replace Branch free-text geography placeholder with normalized FKs"
```

---

### Task 8: Api.Tests — End-to-End Geography and DeliveryZone Tests

**Files:**
- Create: `tests/Api.Tests/GeographyEndpointsTests.cs`
- Create: `tests/Api.Tests/DeliveryZonesEndpointsTests.cs`

**Interfaces:**
- Consumes: `GeographyEndpoints`/`DeliveryZonesEndpoints` (Task 5), `GeographyIds` (Task 2).
- Produces: end-to-end proof that geography lookups work off seeded data, `DeliveryZone` CRUD
  works, and `DeliveryZone` is tenant-isolated like `Branch` — mirrors `TenantIsolationTests.cs`'s
  two-org pattern.

- [ ] **Step 1: Write GeographyEndpointsTests**

`tests/Api.Tests/GeographyEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class GeographyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GeographyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Geo Org {suffix}", $"geo-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetProvinces_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/geography/provinces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProvinces_Authenticated_ReturnsAllSeven()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/geography/provinces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var provinces = await response.Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        Assert.Equal(7, provinces!.Count);
    }

    [Fact]
    public async Task GetDistricts_FiltersByProvince()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var provinces = await (await client.GetAsync("/api/v1/geography/provinces")).Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        var bagmati = provinces!.Single(p => p.Name == "Bagmati");

        var response = await client.GetAsync($"/api/v1/geography/districts?provinceId={bagmati.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var districts = await response.Content.ReadFromJsonAsync<List<DistrictResponse>>();
        Assert.Equal(13, districts!.Count);
        Assert.Contains(districts, d => d.Name == "Kathmandu");
    }

    [Fact]
    public async Task GetLocalLevels_ThenWards_ThenAreas_WalksFullChain()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var provinces = await (await client.GetAsync("/api/v1/geography/provinces")).Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        var bagmati = provinces!.Single(p => p.Name == "Bagmati");
        var districts = await (await client.GetAsync($"/api/v1/geography/districts?provinceId={bagmati.Id}")).Content.ReadFromJsonAsync<List<DistrictResponse>>();
        var kathmandu = districts!.Single(d => d.Name == "Kathmandu");

        var localLevelsResponse = await client.GetAsync($"/api/v1/geography/local-levels?districtId={kathmandu.Id}");
        Assert.Equal(HttpStatusCode.OK, localLevelsResponse.StatusCode);
        var localLevels = await localLevelsResponse.Content.ReadFromJsonAsync<List<LocalLevelResponse>>();
        var kathmanduMetro = Assert.Single(localLevels!);
        Assert.Equal("Kathmandu Metropolitan City", kathmanduMetro.Name);

        var wardsResponse = await client.GetAsync($"/api/v1/geography/wards?localLevelId={kathmanduMetro.Id}");
        Assert.Equal(HttpStatusCode.OK, wardsResponse.StatusCode);
        var wards = await wardsResponse.Content.ReadFromJsonAsync<List<WardResponse>>();
        Assert.Equal(3, wards!.Count);

        var areasResponse = await client.GetAsync($"/api/v1/geography/areas?wardId={wards[0].Id}");
        Assert.Equal(HttpStatusCode.OK, areasResponse.StatusCode);
        var areas = await areasResponse.Content.ReadFromJsonAsync<List<AreaResponse>>();
        Assert.Equal(2, areas!.Count);
    }
}
```

- [ ] **Step 2: Write DeliveryZonesEndpointsTests**

`tests/Api.Tests/DeliveryZonesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class DeliveryZonesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly List<Guid> KathmanduWard1Areas = new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    public DeliveryZonesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Zone Org {suffix}", $"zone-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task CreateThenGet_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "a");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Kathmandu Ward 1", KathmanduWard1Areas));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var getResponse = await client.GetAsync($"/api/v1/delivery-zones/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        Assert.Equal("Kathmandu Ward 1", fetched!.Name);
        Assert.Equal(2, fetched.CoveredAreaIds.Count);
    }

    [Fact]
    public async Task CreateDeliveryZone_UnknownAreaId_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "b");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Bad Zone", new List<Guid> { Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDeliveryZone_ChangesNameAndCoverage()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "c");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Original", KathmanduWard1Areas));
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var ward2Areas = new List<Guid> { GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 2, "Area A") };
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/delivery-zones/{created!.Id}",
            new UpdateDeliveryZoneRequest("Renamed", ward2Areas));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(ward2Areas, updated.CoveredAreaIds);
    }

    [Fact]
    public async Task GetDeliveryZone_OrgBCannotSeeOrgAsZone()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "d");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Org A Zone", KathmanduWard1Areas));
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var orgBClient = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(orgBClient, "e");
        orgBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var getResponse = await orgBClient.GetAsync($"/api/v1/delivery-zones/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await orgBClient.GetAsync("/api/v1/delivery-zones");
        var orgBZones = await listResponse.Content.ReadFromJsonAsync<List<DeliveryZoneResponse>>();
        Assert.DoesNotContain(orgBZones!, z => z.Id == created.Id);
    }
}
```

- [ ] **Step 3: Run the full test suite**

```bash
dotnet test Courier.slnx
```

Expected: all tests pass, including the 7 new tests in this task.

- [ ] **Step 4: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add tests/Api.Tests/GeographyEndpointsTests.cs tests/Api.Tests/DeliveryZonesEndpointsTests.cs
git commit -m "test: add end-to-end geography lookup and delivery-zone endpoint tests"
```

---

### Task 9: Update ROADMAP.md Status

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an updated Phase 3 status note, mirroring Phase 1/2's, so Phase 4's "Entry" check has
  a clear record of what's actually done.

- [ ] **Step 1: Add the status note**

In `docs/ROADMAP.md`, immediately after the existing Phase 3 paragraph:

```text
## Phase 3 — Nepal Geography, Zones, Coverage

`Province, District, LocalLevel, Ward, Area, DeliveryZone` (`CLARIFICATIONS.md` #2). Branch
geography fields become FKs into this module, replacing any earlier free-text placeholder.
*Exit:* branch coverage/zone lookups work off normalized data, no free-text geography remains
on `Branch`.
```

add:

```text

*Status (2026-08-18): Complete.* A new `Geography` module implements `Province, District,
LocalLevel(Type), Ward, Area, DeliveryZone` per `DOMAIN-MODEL.md`. All 7 real provinces and all
77 real districts are seeded; `LocalLevel/Ward/Area` are seeded for the 5 districts CLAUDE.md §1's
example org operates in (Kathmandu, Lalitpur, Bhaktapur, Kaski, Rupandehi) — full national
local-level coverage is an explicit follow-up data-loading task, not part of this phase's
application-logic scope. `Branch` now carries `ProvinceId/DistrictId/LocalLevelId/WardNumber` FKs
in place of the Phase 2 free-text placeholder; `BranchService` validates the full chain
server-side via `IGeographyLookupService` before persisting (`tests/Api.Tests/
BranchesEndpointsTests.cs`'s `CreateBranch_MismatchedGeographyChain_Returns400`).
`GET /api/v1/geography/{provinces,districts,local-levels,wards,areas}` (permission
`geography.read`) and `POST/GET/PUT /api/v1/delivery-zones` (`zone.read/create/update`, tenant-
isolated like `Branch`) are exercised end-to-end in `tests/Api.Tests/GeographyEndpointsTests.cs`
and `tests/Api.Tests/DeliveryZonesEndpointsTests.cs`.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 3 exit criteria met"
```

---

## Plan Self-Review Notes

- **Spec coverage:** `Province/District/LocalLevel/Ward/Area/DeliveryZone` (Task 2), Branch FKs
  replacing free-text (Task 7), branch coverage/zone lookups off normalized data (Tasks 3, 5, 8),
  no free-text geography remains on `Branch` (Task 7 removes the old `AddressLine`-only comment
  and adds required FKs) — all of `ROADMAP.md` Phase 3's stated scope is covered.
- **Seed data scope:** matches the approved 2026-08-18 decision exactly (full provinces/districts,
  partial local levels/wards/areas for the 5-district example-org footprint) — verified by Task
  2's row-count test (7/77/5/15/30).
- **Branch FK nullability:** matches the approved 2026-08-18 decision — all four fields are
  `required` (non-nullable), Phase 2's existing tests are updated rather than left nullable.
- **Type/signature consistency:** `IGeographyLookupService.IsValidHierarchyAsync(Guid, Guid, Guid,
  int, CancellationToken)` has the identical signature everywhere it is defined (Task 3),
  implemented (Task 3), and called (Task 7). `CreateBranchRequest`/`BranchResponse` field order
  (`ProvinceId, DistrictId, LocalLevelId, WardNumber` right after `Type`) is identical across
  Task 7's `BranchDtos.cs`, `BranchService.cs`, and both rewritten test files.

