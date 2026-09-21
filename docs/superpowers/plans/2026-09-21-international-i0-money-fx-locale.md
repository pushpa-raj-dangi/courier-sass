# International I0 — Money, FX and Tenant Locale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `Money`/`CurrencyCode` value types, an append-only audited `FxRate` store with a conversion service, and tenant `DefaultLocale`/`CalendarPreference` settings — the foundation every later international step (I1–I8) builds on.

**Architecture:** `Money` and `CurrencyCode` are dependency-free value objects in `SharedKernel`. `FxRate` is platform-level (not tenant-owned, no query filter) reference data owned by the existing `Pricing` module, edited only by SuperAdmin via audited endpoints, and consumed by other modules through `IFxConversionService`. Locale settings extend the existing `OrganizationSettings` row and profile DTO.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core + Npgsql, xUnit, EF InMemory for module unit tests, shared Postgres for `Api.Tests`.

**Spec:** `docs/superpowers/specs/2026-09-21-international-support-design.md` (as amended 2026-09-21: no currency-column backfill; manual FX entry only).

## Global Constraints

- Modular monolith; new code follows `Module/{Domain,Application,Infrastructure,Presentation}`. No new library or module.
- Business logic in domain/application services, never in endpoints.
- Every existing persisted amount is NPR by invariant. **Do not add currency columns to existing tables.**
- `FxRate` is append-only: no update or delete path anywhere.
- FX endpoints are SuperAdmin-only (`RequireSuperAdmin`). Platform-level audit entries use `OrganizationId = null`, matching `PlatformSettingsService`.
- Migrations are additive only. Shared dev Postgres is `courier-postgres-1` on port 5434 (reuse it; do not start a second one).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Build and tests must be green before each commit that follows a test run.

## File Structure

| File | Responsibility |
|---|---|
| Create `src/SharedKernel/CurrencyCode.cs` | Validated three-letter currency code value object |
| Create `src/SharedKernel/Money.cs` | Immutable amount + currency, same-currency arithmetic, conversion with explicit rate; `CurrencyMismatchException` |
| Create `tests/SharedKernel.Tests/CurrencyCodeTests.cs`, `MoneyTests.cs` | Value-object unit tests |
| Create `src/Modules/Pricing/Domain/FxRate.cs` | Append-only rate row |
| Create `src/Modules/Pricing/Infrastructure/FxRateConfiguration.cs` | EF mapping, index, check constraints |
| Modify `src/Persistence/AppDbContext.cs` | `DbSet<FxRate>` + `ApplyConfiguration` |
| Create `src/Modules/Pricing/Application/FxRateService.cs` | Record, list, latest, convert; `IFxConversionService`; DTOs and exceptions |
| Create `tests/Pricing.Tests/FxRateServiceTests.cs` | Service unit tests |
| Create `src/Modules/Pricing/Presentation/FxRateEndpoints.cs` | SuperAdmin list/record endpoints |
| Modify `src/Api/Program.cs` | DI + endpoint mapping |
| Create `tests/Api.Tests/FxRateEndpointsTests.cs` | Endpoint auth/validation/persistence tests |
| Create `src/Modules/Identity/Domain/OrganizationLocaleOptions.cs` | Supported locales/calendars |
| Modify `src/Modules/Identity/Domain/OrganizationSettings.cs`, `Infrastructure/OrganizationSettingsConfiguration.cs`, `Application/OrganizationSettingsDtos.cs`, `Application/OrganizationSettingsService.cs` | Locale fields, validation, mapping |
| Modify `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`, `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs` | Locale tests |
| Create `docs/INTERNATIONAL.md`; modify `docs/DOMAIN-MODEL.md` | Documentation |

---

### Task 1: `CurrencyCode` and `Money` value objects

**Files:**
- Create: `src/SharedKernel/CurrencyCode.cs`
- Create: `src/SharedKernel/Money.cs`
- Test: `tests/SharedKernel.Tests/CurrencyCodeTests.cs`
- Test: `tests/SharedKernel.Tests/MoneyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (later tasks rely on these exact names):
  - `sealed record CurrencyCode` with `string Value`, `static CurrencyCode Npr`, `static CurrencyCode Parse(string)`, `static bool TryParse(string?, out CurrencyCode)`.
  - `sealed record Money` with `decimal Amount`, `CurrencyCode Currency`, ctor `Money(decimal, CurrencyCode)`, `static Money Zero(CurrencyCode)`, `static Money Npr(decimal)`, `Add`, `Subtract`, `Multiply(decimal)`, `ConvertTo(CurrencyCode target, decimal rate)`, operators `+` and `-`.
  - `sealed class CurrencyMismatchException : InvalidOperationException`.

- [ ] **Step 1: Write the failing `CurrencyCode` tests**

`tests/SharedKernel.Tests/CurrencyCodeTests.cs`:

```csharp
using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("USD", "USD")]
    [InlineData("usd", "USD")]
    [InlineData("nPr", "NPR")]
    public void Parse_ValidCode_NormalizesToUpperCase(string input, string expected)
    {
        Assert.Equal(expected, CurrencyCode.Parse(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    [InlineData("US ")]
    public void TryParse_InvalidCode_ReturnsFalse(string? input)
    {
        Assert.False(CurrencyCode.TryParse(input, out _));
    }

    [Fact]
    public void Parse_InvalidCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyCode.Parse("12"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(CurrencyCode.Parse("usd"), CurrencyCode.Parse("USD"));
        Assert.NotEqual(CurrencyCode.Npr, CurrencyCode.Parse("USD"));
    }

    [Fact]
    public void ToString_ReturnsCode() => Assert.Equal("NPR", CurrencyCode.Npr.ToString());
}
```

- [ ] **Step 2: Write the failing `Money` tests**

`tests/SharedKernel.Tests/MoneyTests.cs`:

```csharp
using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class MoneyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        var result = Money.Npr(100m).Add(Money.Npr(50.25m));
        Assert.Equal(new Money(150.25m, CurrencyCode.Npr), result);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Npr(1m).Add(new Money(1m, Usd)));
    }

    [Fact]
    public void Subtract_DifferentCurrency_Throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Npr(1m).Subtract(new Money(1m, Usd)));
    }

    [Fact]
    public void Operators_DelegateToAddAndSubtract()
    {
        Assert.Equal(Money.Npr(3m), Money.Npr(1m) + Money.Npr(2m));
        Assert.Equal(Money.Npr(1m), Money.Npr(3m) - Money.Npr(2m));
    }

    [Fact]
    public void Multiply_ScalesAmountAndKeepsCurrency()
    {
        Assert.Equal(new Money(25m, Usd), new Money(10m, Usd).Multiply(2.5m));
    }

    [Fact]
    public void ConvertTo_AppliesRateAndRoundsToTwoDecimalsAwayFromZero()
    {
        var converted = new Money(100m, Usd).ConvertTo(CurrencyCode.Npr, 133.4567m);
        Assert.Equal(new Money(13345.67m, CurrencyCode.Npr), converted);

        var halfway = new Money(1m, Usd).ConvertTo(CurrencyCode.Npr, 0.005m);
        Assert.Equal(0.01m, halfway.Amount);
    }

    [Fact]
    public void ConvertTo_SameCurrency_ReturnsSameValue()
    {
        var value = Money.Npr(10m);
        Assert.Equal(value, value.ConvertTo(CurrencyCode.Npr, 5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConvertTo_NonPositiveRate_Throws(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1m, Usd).ConvertTo(CurrencyCode.Npr, (decimal)rate));
    }

    [Fact]
    public void Constructor_NullCurrency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));
    }

    [Fact]
    public void Zero_HasZeroAmountInGivenCurrency()
    {
        Assert.Equal(new Money(0m, Usd), Money.Zero(Usd));
    }

    [Fact]
    public void ToString_UsesInvariantTwoDecimalFormat()
    {
        Assert.Equal("1234.50 NPR", Money.Npr(1234.5m).ToString());
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~CurrencyCodeTests|FullyQualifiedName~MoneyTests"`
Expected: build FAIL — `CurrencyCode`, `Money`, `CurrencyMismatchException` do not exist.

- [ ] **Step 4: Implement `CurrencyCode`**

`src/SharedKernel/CurrencyCode.cs`:

```csharp
namespace Courier.SharedKernel;

// Validates ISO-4217 *format* only (three ASCII letters, normalized to upper case). A membership
// list is deliberately out of scope: carriers may quote in currencies we have not enumerated.
// A sealed record class (not a struct) so default(CurrencyCode) cannot silently produce an
// invalid value.
public sealed record CurrencyCode
{
    public string Value { get; }

    private CurrencyCode(string value) => Value = value;

    public static CurrencyCode Npr { get; } = new("NPR");

    public static CurrencyCode Parse(string value) =>
        TryParse(value, out var code)
            ? code
            : throw new ArgumentException($"'{value}' is not a valid three-letter currency code.", nameof(value));

    public static bool TryParse(string? value, out CurrencyCode code)
    {
        code = default!;
        if (value is null || value.Length != 3)
        {
            return false;
        }

        var upper = value.ToUpperInvariant();
        foreach (var c in upper)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        code = new CurrencyCode(upper);
        return true;
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 4b: Implement `Money`**

`src/SharedKernel/Money.cs`:

```csharp
using System.Globalization;

namespace Courier.SharedKernel;

public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(CurrencyCode left, CurrencyCode right)
        : base($"Cannot combine {left} and {right} amounts without an explicit conversion.") { }
}

// Immutable amount + currency. Arithmetic is only defined within one currency; crossing
// currencies requires ConvertTo with an explicit rate (callers record the FxRate id they used —
// see FxRateService.ConvertAsync). ConvertTo rounds to two decimals: every conversion target in
// this product today is NPR.
public sealed record Money
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    public Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
    }

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public static Money Npr(decimal amount) => new(amount, CurrencyCode.Npr);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money ConvertTo(CurrencyCode target, decimal rate)
    {
        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Conversion rate must be positive.");
        }
        if (target == Currency)
        {
            return this;
        }

        return new Money(Math.Round(Amount * rate, 2, MidpointRounding.AwayFromZero), target);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public override string ToString() => $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~CurrencyCodeTests|FullyQualifiedName~MoneyTests"`
Expected: PASS (all tests in both classes).

- [ ] **Step 6: Commit**

```bash
git add src/SharedKernel/CurrencyCode.cs src/SharedKernel/Money.cs tests/SharedKernel.Tests/CurrencyCodeTests.cs tests/SharedKernel.Tests/MoneyTests.cs
git commit -m "$(cat <<'EOF'
feat(sharedkernel): add Money and CurrencyCode value objects

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `FxRate` entity, mapping and migration

**Files:**
- Create: `src/Modules/Pricing/Domain/FxRate.cs`
- Create: `src/Modules/Pricing/Infrastructure/FxRateConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs` (add `DbSet` next to `PlatformSettings` at ~line 110; add `ApplyConfiguration` next to `AdditionalChargeConfiguration` at ~line 152)
- Create (generated): `src/Persistence/Migrations/<timestamp>_AddFxRates.cs` and `.Designer.cs`; update `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (the entity stores plain `string` currency codes; validation happens in the service).
- Produces: `Courier.Pricing.Domain.FxRate { Guid Id; string FromCurrency; string ToCurrency; decimal Rate; DateTimeOffset EffectiveAt; string Source; Guid? CreatedByUserId; DateTimeOffset CreatedAt }` and `Courier.Pricing.Infrastructure.FxRateConfiguration`.

- [ ] **Step 1: Create the entity**

`src/Modules/Pricing/Domain/FxRate.cs`:

```csharp
namespace Courier.Pricing.Domain;

// Platform-level reference data: NOT tenant-owned, so it has no OrganizationId and no query
// filter. Append-only — a correction is a new row with a later EffectiveAt, never an edit, so
// every conversion that recorded an FxRate.Id stays reproducible. "Current" for a pair is the
// row with the greatest EffectiveAt <= the as-of instant (ties broken by CreatedAt).
// Rate means: 1 unit of FromCurrency = Rate units of ToCurrency.
public class FxRate
{
    public Guid Id { get; set; }
    public string FromCurrency { get; set; } = default!;
    public string ToCurrency { get; set; } = default!;
    public decimal Rate { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string Source { get; set; } = default!;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

`src/Modules/Pricing/Infrastructure/FxRateConfiguration.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("FxRates", t =>
        {
            t.HasCheckConstraint("CK_FxRates_Rate_Positive", "\"Rate\" > 0");
            t.HasCheckConstraint("CK_FxRates_Distinct_Currencies", "\"FromCurrency\" <> \"ToCurrency\"");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, 8);
        builder.Property(r => r.Source).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.EffectiveAt });
    }
}
```

- [ ] **Step 3: Register in `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add after the `PlatformSettings` DbSet line:

```csharp
    public DbSet<FxRate> FxRates => Set<FxRate>();
```

and after `modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());` add:

```csharp
        modelBuilder.ApplyConfiguration(new FxRateConfiguration());
```

Do **not** add a `HasQueryFilter` for `FxRate` (it is global data). `using Courier.Pricing.Domain;` and `using Courier.Pricing.Infrastructure;` are already imported (other pricing entities are registered there); add them only if the build says otherwise.

- [ ] **Step 4: Build**

Run: `dotnet build src/Persistence`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Generate the migration**

Make sure the shared Postgres is up: `docker ps --format '{{.Names}} {{.Ports}}'` must list `courier-postgres-1` on `5434`. If it is not running: `docker start courier-postgres-1`.

Run:
```bash
dotnet tool restore
dotnet ef migrations add AddFxRates --project src/Persistence
```
Expected: creates `src/Persistence/Migrations/<timestamp>_AddFxRates.cs` and updates the snapshot.

- [ ] **Step 6: Review the generated migration**

Open the generated `Up`. It must contain **only**: `CreateTable("FxRates", ...)` with the two check constraints and `decimal(18,8)` for `Rate`, plus `CreateIndex` on `(FromCurrency, ToCurrency, EffectiveAt)`. If it touches any other table, stop and investigate the snapshot drift before continuing. `Down` must be `DropTable("FxRates")`.

- [ ] **Step 7: Apply and verify**

Run: `dotnet ef database update --project src/Persistence`
Expected: `Applying migration '..._AddFxRates'. Done.`

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Pricing/Domain/FxRate.cs src/Modules/Pricing/Infrastructure/FxRateConfiguration.cs src/Persistence
git commit -m "$(cat <<'EOF'
feat(pricing): add append-only FxRate entity and migration

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `FxRateService` (record, list, latest, convert)

**Files:**
- Create: `src/Modules/Pricing/Application/FxRateService.cs`
- Test: `tests/Pricing.Tests/FxRateServiceTests.cs`

**Interfaces:**
- Consumes: `Courier.SharedKernel.Money`, `CurrencyCode`, `PagedResult<T>`, `PaginationDefaults`; `Courier.Audit.Application.IAuditLogger`/`AuditEntry`; `Courier.Pricing.Domain.FxRate`; `FxRateConfiguration`.
- Produces:
  - `record FxRateDto(Guid Id, string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset EffectiveAt, string Source, Guid? CreatedByUserId, DateTimeOffset CreatedAt)`
  - `record RecordFxRateRequest(string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset? EffectiveAt)`
  - `record FxConversion(Money Source, Money Converted, Guid? FxRateId, decimal Rate)`
  - `class FxRateValidationException : Exception`, `class FxRateNotFoundException : Exception`
  - `interface IFxConversionService { Task<FxConversion> ConvertAsync(Money source, CurrencyCode target, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default); }`
  - `class FxRateService : IFxConversionService` with `RecordAsync(RecordFxRateRequest, Guid actingUserId, CancellationToken)`, `GetLatestAsync(CurrencyCode from, CurrencyCode to, DateTimeOffset? asOf, CancellationToken)`, `ListAsync(string? from, string? to, int? page, int? pageSize, CancellationToken)`.
  - Constructor: `FxRateService(DbContext dbContext, IAuditLogger auditLogger)`.

- [ ] **Step 1: Write the failing tests**

`tests/Pricing.Tests/FxRateServiceTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

public class FxRateServiceTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyConfiguration(new FxRateConfiguration());
    }

    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static (FxRateService Service, TestDbContext Db, CapturingAuditLogger Audit) Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"fx-{Guid.NewGuid()}")
            .Options;
        var db = new TestDbContext(options);
        var audit = new CapturingAuditLogger();
        return (new FxRateService(db, audit), db, audit);
    }

    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    [Fact]
    public async Task RecordAsync_ValidRequest_PersistsRowAndAudits()
    {
        var (service, db, audit) = Create();

        var dto = await service.RecordAsync(new RecordFxRateRequest("usd", "npr", 133.5m, null), Admin);

        Assert.Equal("USD", dto.FromCurrency);
        Assert.Equal("NPR", dto.ToCurrency);
        Assert.Equal(133.5m, dto.Rate);
        Assert.Equal("manual", dto.Source);
        Assert.Equal(Admin, dto.CreatedByUserId);
        Assert.Single(db.Set<FxRate>());
        var entry = Assert.Single(audit.Entries);
        Assert.Null(entry.OrganizationId);
        Assert.Equal(Admin, entry.UserId);
        Assert.Equal("fx_rate.recorded", entry.Action);
        Assert.Equal("FxRate", entry.EntityType);
        Assert.Equal(dto.Id.ToString(), entry.EntityId);
    }

    [Theory]
    [InlineData("US", "NPR", 1)]
    [InlineData("USD", "N1R", 1)]
    [InlineData("USD", "USD", 1)]
    [InlineData("USD", "NPR", 0)]
    [InlineData("USD", "NPR", -5)]
    public async Task RecordAsync_InvalidInput_Throws(string from, string to, double rate)
    {
        var (service, db, _) = Create();

        await Assert.ThrowsAsync<FxRateValidationException>(() =>
            service.RecordAsync(new RecordFxRateRequest(from, to, (decimal)rate, null), Admin));

        Assert.Empty(db.Set<FxRate>());
    }

    [Fact]
    public async Task RecordAsync_EffectiveAtInTheFuture_Throws()
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<FxRateValidationException>(() =>
            service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 133m, DateTimeOffset.UtcNow.AddHours(1)), Admin));
    }

    [Fact]
    public async Task RecordAsync_SecondRateForSamePair_AppendsAndKeepsHistory()
    {
        var (service, db, _) = Create();
        var earlier = DateTimeOffset.UtcNow.AddDays(-2);

        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, earlier), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, null), Admin);

        Assert.Equal(2, db.Set<FxRate>().Count());
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsMostRecentEffectiveRate()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-2)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var latest = await service.GetLatestAsync(Usd, CurrencyCode.Npr, null);

        Assert.Equal(135m, latest!.Rate);
    }

    [Fact]
    public async Task GetLatestAsync_AsOfInThePast_IgnoresLaterRates()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-2)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var asOf = await service.GetLatestAsync(Usd, CurrencyCode.Npr, DateTimeOffset.UtcNow.AddDays(-1.5));

        Assert.Equal(130m, asOf!.Rate);
    }

    [Fact]
    public async Task GetLatestAsync_NoRate_ReturnsNull()
    {
        var (service, _, _) = Create();

        Assert.Null(await service.GetLatestAsync(Usd, CurrencyCode.Npr, null));
    }

    [Fact]
    public async Task GetLatestAsync_DoesNotUseTheInversePair()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("NPR", "USD", 0.0075m, null), Admin);

        Assert.Null(await service.GetLatestAsync(Usd, CurrencyCode.Npr, null));
    }

    [Fact]
    public async Task ConvertAsync_UsesLatestRateAndReportsRateId()
    {
        var (service, _, _) = Create();
        var recorded = await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 133.4567m, null), Admin);

        var conversion = await service.ConvertAsync(new Money(100m, Usd), CurrencyCode.Npr);

        Assert.Equal(new Money(13345.67m, CurrencyCode.Npr), conversion.Converted);
        Assert.Equal(new Money(100m, Usd), conversion.Source);
        Assert.Equal(recorded.Id, conversion.FxRateId);
        Assert.Equal(133.4567m, conversion.Rate);
    }

    [Fact]
    public async Task ConvertAsync_SameCurrency_ReturnsIdentityWithoutRateId()
    {
        var (service, _, _) = Create();

        var conversion = await service.ConvertAsync(Money.Npr(50m), CurrencyCode.Npr);

        Assert.Equal(Money.Npr(50m), conversion.Converted);
        Assert.Null(conversion.FxRateId);
        Assert.Equal(1m, conversion.Rate);
    }

    [Fact]
    public async Task ConvertAsync_NoRate_ThrowsNotFound()
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<FxRateNotFoundException>(() =>
            service.ConvertAsync(new Money(1m, Usd), CurrencyCode.Npr));
    }

    [Fact]
    public async Task ListAsync_FiltersByPairAndPagesNewestFirst()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-3)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("EUR", "NPR", 145m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var page = await service.ListAsync("usd", "npr", 1, 10);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { 135m, 130m }, page.Items.Select(i => i.Rate).ToArray());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pricing.Tests --filter "FullyQualifiedName~FxRateServiceTests"`
Expected: build FAIL — `FxRateService`, `RecordFxRateRequest`, etc. do not exist.

- [ ] **Step 3: Implement the service**

`src/Modules/Pricing/Application/FxRateService.cs`:

```csharp
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public sealed record FxRateDto(Guid Id, string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset EffectiveAt, string Source, Guid? CreatedByUserId, DateTimeOffset CreatedAt);

public sealed record RecordFxRateRequest(string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset? EffectiveAt);

// FxRateId is null only for a same-currency "conversion" (identity), where no rate is involved.
public sealed record FxConversion(Money Source, Money Converted, Guid? FxRateId, decimal Rate);

public class FxRateValidationException : Exception
{
    public FxRateValidationException(string message) : base(message) { }
}

public class FxRateNotFoundException : Exception
{
    public FxRateNotFoundException(CurrencyCode from, CurrencyCode to)
        : base($"No FX rate is available for {from} to {to}.") { }
}

// Consumed by other modules (international pricing) so they never touch the FxRates table.
public interface IFxConversionService
{
    Task<FxConversion> ConvertAsync(Money source, CurrencyCode target, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default);
}

public class FxRateService : IFxConversionService
{
    // Absorbs clock skew between an operator's browser/server and this host; anything further
    // ahead is almost certainly a typo, and a future-dated rate would be silently ignored by
    // GetLatestAsync until then, which is a confusing failure mode.
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);
    private const string ManualSource = "manual";

    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public FxRateService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<FxRateDto> RecordAsync(RecordFxRateRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        if (!CurrencyCode.TryParse(request.FromCurrency, out var from))
        {
            throw new FxRateValidationException("From currency must be a three-letter currency code.");
        }
        if (!CurrencyCode.TryParse(request.ToCurrency, out var to))
        {
            throw new FxRateValidationException("To currency must be a three-letter currency code.");
        }
        if (from == to)
        {
            throw new FxRateValidationException("From and to currencies must differ.");
        }
        if (request.Rate <= 0m)
        {
            throw new FxRateValidationException("Rate must be greater than zero.");
        }

        var now = DateTimeOffset.UtcNow;
        var effectiveAt = (request.EffectiveAt ?? now).ToUniversalTime();
        if (effectiveAt > now + MaxFutureSkew)
        {
            throw new FxRateValidationException("Effective time cannot be in the future.");
        }

        var rate = new FxRate
        {
            Id = Guid.NewGuid(),
            FromCurrency = from.Value,
            ToCurrency = to.Value,
            Rate = request.Rate,
            EffectiveAt = effectiveAt,
            Source = ManualSource,
            CreatedByUserId = actingUserId,
            CreatedAt = now,
        };
        _dbContext.Set<FxRate>().Add(rate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = ToDto(rate);
        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "fx_rate.recorded", "FxRate", rate.Id.ToString(),
            Before: null, After: JsonSerializer.Serialize(dto)), cancellationToken);

        return dto;
    }

    public async Task<FxRate?> GetLatestAsync(CurrencyCode from, CurrencyCode to, DateTimeOffset? asOf, CancellationToken cancellationToken = default)
    {
        var instant = (asOf ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return await _dbContext.Set<FxRate>()
            .Where(r => r.FromCurrency == from.Value && r.ToCurrency == to.Value && r.EffectiveAt <= instant)
            .OrderByDescending(r => r.EffectiveAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FxConversion> ConvertAsync(Money source, CurrencyCode target, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default)
    {
        if (source.Currency == target)
        {
            return new FxConversion(source, source, null, 1m);
        }

        var rate = await GetLatestAsync(source.Currency, target, asOf, cancellationToken)
            ?? throw new FxRateNotFoundException(source.Currency, target);
        return new FxConversion(source, source.ConvertTo(target, rate.Rate), rate.Id, rate.Rate);
    }

    public async Task<PagedResult<FxRateDto>> ListAsync(string? from, string? to, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var query = _dbContext.Set<FxRate>().AsQueryable();
        if (CurrencyCode.TryParse(from, out var fromCode))
        {
            query = query.Where(r => r.FromCurrency == fromCode.Value);
        }
        if (CurrencyCode.TryParse(to, out var toCode))
        {
            query = query.Where(r => r.ToCurrency == toCode.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(r => r.EffectiveAt)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<FxRateDto>(rows.Select(ToDto).ToList(), normalizedPage, normalizedPageSize, total);
    }

    private static FxRateDto ToDto(FxRate r) =>
        new(r.Id, r.FromCurrency, r.ToCurrency, r.Rate, r.EffectiveAt, r.Source, r.CreatedByUserId, r.CreatedAt);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pricing.Tests --filter "FullyQualifiedName~FxRateServiceTests"`
Expected: PASS (all tests). Then run the whole project to confirm no regression: `dotnet test tests/Pricing.Tests` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Pricing/Application/FxRateService.cs tests/Pricing.Tests/FxRateServiceTests.cs
git commit -m "$(cat <<'EOF'
feat(pricing): add audited FxRateService with conversion

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: SuperAdmin FX endpoints and wiring

**Files:**
- Create: `src/Modules/Pricing/Presentation/FxRateEndpoints.cs`
- Modify: `src/Api/Program.cs` (DI near line 179 `builder.Services.AddScoped<PricingService>();`; mapping near line 435 `app.MapPricingEndpoints();`)
- Test: `tests/Api.Tests/FxRateEndpointsTests.cs`

**Interfaces:**
- Consumes: `FxRateService`, `IFxConversionService`, `FxRateValidationException`, `RecordFxRateRequest` (Task 3); `RequireSuperAdmin()` and `ClaimsPrincipal.GetUserId()` from `Courier.SharedKernel`.
- Produces: `GET /api/v1/platform/fx-rates?from&to&page&pageSize` → `PagedResult<FxRateDto>`; `POST /api/v1/platform/fx-rates` (body `RecordFxRateRequest`) → `201` with `FxRateDto`, or `400 { error: { code: "FX_RATE_VALIDATION_FAILED", message } }`. DI: `FxRateService` and `IFxConversionService` scoped.

- [ ] **Step 1: Write the failing endpoint tests**

`tests/Api.Tests/FxRateEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class FxRateEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FxRateEndpointsTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task List_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform/fx-rates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = "USD", toCurrency = "NPR", rate = 133m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_AsSuperAdmin_CreatesRateAndListsIt()
    {
        var client = await SuperAdminClientAsync();

        // FxRates is append-only by design, so this row cannot be cleaned up afterwards. ISO 4217
        // XTS is the code reserved for testing, so it can never collide with a real pair.
        var rate = 100m + (decimal)(Random.Shared.Next(1, 90000)) / 1000m;
        var post = await client.PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = "xts", toCurrency = "NPR", rate });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("XTS", created.GetProperty("fromCurrency").GetString());

        var list = await client.GetAsync("/api/v1/platform/fx-rates?from=XTS&to=NPR&page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.GetProperty("id").GetString(), body.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("USD", "USD", 1)]
    [InlineData("US", "NPR", 1)]
    [InlineData("USD", "NPR", 0)]
    public async Task Record_InvalidInput_ReturnsBadRequestWithErrorContract(string from, string to, double rate)
    {
        var client = await SuperAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = from, toCurrency = to, rate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FX_RATE_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~FxRateEndpointsTests"`
Expected: FAIL — routes return 404 (unauthenticated tests expect 401, SuperAdmin tests expect 201/200/400).

- [ ] **Step 3: Implement the endpoints**

`src/Modules/Pricing/Presentation/FxRateEndpoints.cs`:

```csharp
using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Pricing.Presentation;

public static class FxRateEndpoints
{
    public static void MapFxRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/fx-rates").WithTags("FX Rates");

        group.MapGet("/", async (string? from, string? to, int? page, int? pageSize, FxRateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(from, to, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (RecordFxRateRequest request, FxRateService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.RecordAsync(request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Created($"/api/v1/platform/fx-rates?from={created.FromCurrency}&to={created.ToCurrency}", created);
            }
            catch (FxRateValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "FX_RATE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 4: Wire DI and mapping in `Program.cs`**

After `builder.Services.AddScoped<IServiceTypeLookupService>(sp => sp.GetRequiredService<PricingService>());` add:

```csharp
builder.Services.AddScoped<FxRateService>();
builder.Services.AddScoped<IFxConversionService>(sp => sp.GetRequiredService<FxRateService>());
```

After `app.MapPricingEndpoints();` add:

```csharp
app.MapFxRateEndpoints();
```

(`using Courier.Pricing.Application;` and `using Courier.Pricing.Presentation;` are already present.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~FxRateEndpointsTests"`
Expected: PASS (all tests). Requires the shared Postgres on port 5434.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Pricing/Presentation/FxRateEndpoints.cs src/Api/Program.cs tests/Api.Tests/FxRateEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(pricing): add SuperAdmin FX rate list and record endpoints

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Tenant `DefaultLocale` and `CalendarPreference`

**Files:**
- Create: `src/Modules/Identity/Domain/OrganizationLocaleOptions.cs`
- Modify: `src/Modules/Identity/Domain/OrganizationSettings.cs` (Profile block, after `TimeZoneId`)
- Modify: `src/Modules/Identity/Infrastructure/OrganizationSettingsConfiguration.cs`
- Modify: `src/Modules/Identity/Application/OrganizationSettingsDtos.cs`
- Modify: `src/Modules/Identity/Application/OrganizationSettingsService.cs` (`UpdateProfileAsync`, `ToDto`)
- Create (generated): `src/Persistence/Migrations/<timestamp>_AddOrganizationLocaleSettings.cs`
- Test: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`
- Test: `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`

**Interfaces:**
- Consumes: existing `OrganizationSettingsService`, `OrganizationSettingsValidationException`.
- Produces: `OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours, string? DefaultLocale = null, string? CalendarPreference = null)`. On **read** both new fields are always non-null (`"en"`/`"ne"`, `"Gregorian"`/`"BikramSambat"`). On **update**, `null` means "leave unchanged" (PATCH semantics), so existing clients that omit them do not reset a saved preference. `OrganizationLocaleOptions.SupportedLocales` / `SupportedCalendars` (`IReadOnlyList<string>`).

- [ ] **Step 1: Write the failing service tests**

Add to `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`, inside `OrganizationSettingsServiceTests`, reusing the existing stubs and helpers:

```csharp
    private static OrganizationSettingsService NewService(DbContext dbContext, FakeAuditLogger? audit = null) =>
        new(dbContext, new NullTenantProvider(), audit ?? new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

    [Fact]
    public async Task GetAsync_NewRow_DefaultsToEnglishGregorian()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();

        var result = await NewService(dbContext).GetAsync(organization.Id);

        Assert.Equal("en", result.Profile.DefaultLocale);
        Assert.Equal("Gregorian", result.Profile.CalendarPreference);
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidLocaleAndCalendar_Persists()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = NewService(dbContext);

        var result = await service.UpdateProfileAsync(organization.Id, Guid.NewGuid(),
            new OrganizationProfileDto(null, null, null, null, "Asia/Kathmandu", null, "ne", "BikramSambat"));

        Assert.Equal("ne", result.DefaultLocale);
        Assert.Equal("BikramSambat", result.CalendarPreference);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal("ne", persisted.DefaultLocale);
        Assert.Equal("BikramSambat", persisted.CalendarPreference);
    }

    [Fact]
    public async Task UpdateProfileAsync_NullLocaleAndCalendar_LeavesSavedValuesUnchanged()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = NewService(dbContext);
        await service.UpdateProfileAsync(organization.Id, Guid.NewGuid(),
            new OrganizationProfileDto(null, null, null, null, "Asia/Kathmandu", null, "ne", "BikramSambat"));

        var result = await service.UpdateProfileAsync(organization.Id, Guid.NewGuid(),
            new OrganizationProfileDto("help@example.test", null, null, null, "Asia/Kathmandu", null));

        Assert.Equal("ne", result.DefaultLocale);
        Assert.Equal("BikramSambat", result.CalendarPreference);
        Assert.Equal("help@example.test", result.SupportEmail);
    }

    [Theory]
    [InlineData("fr", "Gregorian")]
    [InlineData("EN", "Gregorian")]
    [InlineData("en", "Julian")]
    public async Task UpdateProfileAsync_UnsupportedLocaleOrCalendar_ThrowsAndPersistsNothing(string locale, string calendar)
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var audit = new FakeAuditLogger();
        var service = NewService(dbContext, audit);

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateProfileAsync(organization.Id, Guid.NewGuid(),
                new OrganizationProfileDto("changed@example.test", null, null, null, "Asia/Kathmandu", null, locale, calendar)));

        var persisted = await service.GetAsync(organization.Id);
        Assert.Null(persisted.Profile.SupportEmail);
        Assert.Empty(audit.Entries);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests --filter "FullyQualifiedName~OrganizationSettingsServiceTests"`
Expected: build FAIL — `OrganizationProfileDto` has no `DefaultLocale`/`CalendarPreference`.

- [ ] **Step 3: Add the options class**

`src/Modules/Identity/Domain/OrganizationLocaleOptions.cs`:

```csharp
namespace Courier.Identity.Domain;

public static class OrganizationLocaleOptions
{
    public const string DefaultLocale = "en";
    public const string DefaultCalendar = "Gregorian";

    public static readonly IReadOnlyList<string> SupportedLocales = new[] { "en", "ne" };
    public static readonly IReadOnlyList<string> SupportedCalendars = new[] { "Gregorian", "BikramSambat" };
}
```

- [ ] **Step 4: Add the entity fields**

In `src/Modules/Identity/Domain/OrganizationSettings.cs`, after `TimeZoneId`:

```csharp
    public string DefaultLocale { get; set; } = OrganizationLocaleOptions.DefaultLocale;
    public string CalendarPreference { get; set; } = OrganizationLocaleOptions.DefaultCalendar;
```

- [ ] **Step 5: Map the fields with column defaults**

In `OrganizationSettingsConfiguration.Configure`, after the `TimeZoneId` line:

```csharp
        builder.Property(s => s.DefaultLocale).HasMaxLength(10).IsRequired().HasDefaultValue(OrganizationLocaleOptions.DefaultLocale);
        builder.Property(s => s.CalendarPreference).HasMaxLength(20).IsRequired().HasDefaultValue(OrganizationLocaleOptions.DefaultCalendar);
```

`HasDefaultValue` is required: without it EF generates `defaultValue: ""` for the new non-nullable columns and existing rows would get invalid empty strings.

- [ ] **Step 6: Extend the DTO**

Replace the `OrganizationProfileDto` line in `OrganizationSettingsDtos.cs`:

```csharp
public sealed record OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours, string? DefaultLocale = null, string? CalendarPreference = null);
```

- [ ] **Step 7: Update the service**

In `OrganizationSettingsService.UpdateProfileAsync`, validate before loading/mutating (first statements of the method), then apply and map:

```csharp
        if (profile.DefaultLocale is not null && !OrganizationLocaleOptions.SupportedLocales.Contains(profile.DefaultLocale))
        {
            throw new OrganizationSettingsValidationException($"Locale must be one of: {string.Join(", ", OrganizationLocaleOptions.SupportedLocales)}.");
        }
        if (profile.CalendarPreference is not null && !OrganizationLocaleOptions.SupportedCalendars.Contains(profile.CalendarPreference))
        {
            throw new OrganizationSettingsValidationException($"Calendar must be one of: {string.Join(", ", OrganizationLocaleOptions.SupportedCalendars)}.");
        }
```

Change the `before` construction to include the current values:

```csharp
        var before = new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours, settings.DefaultLocale, settings.CalendarPreference);
```

After `settings.BusinessHours = profile.BusinessHours;` add:

```csharp
        settings.DefaultLocale = profile.DefaultLocale ?? settings.DefaultLocale;
        settings.CalendarPreference = profile.CalendarPreference ?? settings.CalendarPreference;
```

Update the method's return statement and `ToDto` so both include the two new values:

```csharp
        return new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours, settings.DefaultLocale, settings.CalendarPreference);
```
```csharp
        new OrganizationProfileDto(s.SupportEmail, s.SupportPhone, s.AddressLine, s.LogoUrl, s.TimeZoneId, s.BusinessHours, s.DefaultLocale, s.CalendarPreference),
```

Leave the audit `After:` serialization as-is (`profile`, the request), which keeps existing audit tests valid.

- [ ] **Step 8: Generate and review the migration**

Run: `dotnet ef migrations add AddOrganizationLocaleSettings --project src/Persistence`

The generated `Up` must contain exactly two `AddColumn` calls on `OrganizationSettings` — `DefaultLocale` (`maxLength: 10, nullable: false, defaultValue: "en"`) and `CalendarPreference` (`maxLength: 20, nullable: false, defaultValue: "Gregorian"`) — and nothing else. If `defaultValue: ""` appears, Step 5 was not applied.

- [ ] **Step 9: Run the service tests**

Run: `dotnet test tests/Identity.Tests --filter "FullyQualifiedName~OrganizationSettingsServiceTests"`
Expected: PASS (new and existing tests). The test factory applies the pending migration on first use.

- [ ] **Step 10: Add the API test, run it**

Add to `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`, next to `PatchProfile_OwnerToken_UpdatesAndPersists` (it uses the file's existing `RegisterOrgAsync` helper and the `OrganizationProfileDto`/`OrganizationSettingsDto` types already imported there):

```csharp
    [Fact]
    public async Task PatchProfile_UnsupportedLocale_ReturnsBadRequestWithErrorContract()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = new OrganizationProfileDto(null, null, null, null, "Asia/Kathmandu", null, "fr", null);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", profile);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ORGANIZATION_SETTINGS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PatchProfile_LocaleAndCalendar_PersistAndAreReturned()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = new OrganizationProfileDto(null, null, null, null, "Asia/Kathmandu", null, "ne", "BikramSambat");

        var patchResponse = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", profile);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var body = await (await client.GetAsync("/api/v1/settings/organization")).Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("ne", body!.Profile.DefaultLocale);
        Assert.Equal("BikramSambat", body.Profile.CalendarPreference);
    }
```

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~OrganizationSettingsEndpointsTests"`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Identity src/Persistence tests/Identity.Tests tests/Api.Tests/OrganizationSettingsEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(identity): add tenant DefaultLocale and CalendarPreference settings

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Documentation and full verification

**Files:**
- Create: `docs/INTERNATIONAL.md`
- Modify: `docs/DOMAIN-MODEL.md` (append a section at the end)
- Modify: `docs/ROADMAP.md` (add an "International support" entry pointing to the spec and this plan)

**Interfaces:** none.

- [ ] **Step 1: Create `docs/INTERNATIONAL.md`**

```markdown
# International Support

Design: `docs/superpowers/specs/2026-09-21-international-support-design.md`.
Plans: `docs/superpowers/plans/`.

## Status

| Step | Scope | State |
|---|---|---|
| I0 | Money, FX rates, tenant locale | Done |
| I1–I8 | See the design spec §8 | Planned |

## Money and currency (I0)

- `Courier.SharedKernel.Money` (amount + `CurrencyCode`) is immutable; arithmetic is same-currency only, and a mismatch throws `CurrencyMismatchException`. Crossing currencies requires `ConvertTo` with an explicit rate.
- `CurrencyCode` validates three ASCII letters and normalizes to upper case. It does not check membership in ISO 4217.
- **Every existing persisted amount is NPR.** Merchants are charged in NPR and COD is domestic-only, so existing tables have no currency column. New international entities store an explicit amount and currency per field.

## FX rates (I0)

- `FxRates` is platform-level, append-only, and not tenant-owned. A correction is a new row with a later `EffectiveAt`.
- "Current" for a pair is the row with the greatest `EffectiveAt <= asOf`. Only the exact pair is used; inverse rates are not derived.
- SuperAdmin records rates manually: `POST /api/v1/platform/fx-rates`; list with `GET /api/v1/platform/fx-rates?from=&to=&page=&pageSize=`. Each record is audited (`fx_rate.recorded`, no organization).
- Other modules convert through `IFxConversionService.ConvertAsync`, which returns the converted `Money`, the `FxRateId` used, and the rate, so callers can snapshot them. A missing rate throws `FxRateNotFoundException`.
- An automated rate feed is planned for I5, once a rate source is chosen.

## Tenant locale (I0)

`OrganizationSettings.DefaultLocale` (`en`, `ne`) and `CalendarPreference` (`Gregorian`, `BikramSambat`) are edited through `PATCH /api/v1/settings/organization/profile`. Omitting a field leaves the saved value unchanged. The UI for these settings ships in I1.
```

- [ ] **Step 2: Append to `docs/DOMAIN-MODEL.md`**

Append at the end of the file:

```markdown

## Money, FX and locale (international support, I0)

- `Money` / `CurrencyCode` (SharedKernel): immutable amount + currency, same-currency arithmetic only.
- `FxRate` (Pricing): platform-level, append-only conversion rate; not tenant-owned.
- `OrganizationSettings.DefaultLocale` / `CalendarPreference`: per-tenant UI locale and calendar.
- Existing persisted amounts are NPR by invariant. See `docs/INTERNATIONAL.md`.
```

- [ ] **Step 3: Add the roadmap entry**

In `docs/ROADMAP.md`, add a short "International support" section referencing `docs/INTERNATIONAL.md`, stating I0 is complete and I1–I8 are planned. Match the file's existing heading style (read the file first).

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build Courier.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run every touched test project, then the full suite**

Run:
```bash
dotnet test tests/SharedKernel.Tests
dotnet test tests/Pricing.Tests
dotnet test tests/Identity.Tests
dotnet test tests/Api.Tests
```
Expected: all PASS. Then run `dotnet test Courier.slnx` and confirm no project regressed. Report the pass/fail counts as measured; do not claim green without this output.

- [ ] **Step 6: Security review (tenant and permission)**

Confirm and note in the commit body:
- Every `/api/v1/platform/fx-rates` route uses `RequireSuperAdmin()`; the unauthenticated tests return 401.
- `FxRate` has no tenant column and no query filter by design; there is no update or delete path (grep `FxRate` for `Remove(` and `Update(` — expect none).
- `OrganizationSettings` locale writes go through the existing `org.settings.manage` permission and the tenant-scoped `organizationId` from `ICurrentTenantProvider`.

- [ ] **Step 7: Commit**

```bash
git add docs
git commit -m "$(cat <<'EOF'
docs: document international I0 (money, FX rates, tenant locale)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
