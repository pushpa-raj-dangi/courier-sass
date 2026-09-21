# International I1b — Notification-Template Locale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notification emails render in the organization's default language (`en`/`ne`) with English fallback, and orgs can override the body per locale.

**Architecture:** `NotificationTemplate` gains a `Locale` column. Dispatch resolves the tenant's `DefaultLocale` through an `IOrganizationLocaleLookup` port (adapter in `Courier.Api` reading Identity's `OrganizationSettingsService.GetLocaleAsync`), then a pure resolver picks the body using the spec's four-step fallback. The template admin API and panel gain a locale dimension.

**Tech Stack:** ASP.NET Core / EF Core (PostgreSQL migrations via `dotnet ef`), xUnit (InMemory for unit tests, Postgres for Api tests), Next.js + TanStack Query + Zustand + shadcn/ui + Vitest.

**Spec:** `docs/superpowers/specs/2026-09-21-international-i1b-notification-locale-design.md`

## Global Constraints

- Supported locales are exactly `en` and `ne`; default is `en`. Max length 10 on the column.
- Fallback order (spec §2): (1) org active override for resolved locale, (2) compiled default for resolved locale, (3) org active `en` override, (4) compiled `en` default.
- Notifications must NOT gain a project reference to Identity. Wire the adapter in `src/Api` (pattern: `src/Api/BranchPlanLimitAdapter.cs`).
- Dispatch runs with no tenant context: any read of tenant-owned rows there needs `IgnoreQueryFilters()` plus an explicit `OrganizationId ==` predicate.
- Migration is additive; existing rows backfill to `en`.
- Error code for bad input stays `NOTIFICATION_TEMPLATE_VALIDATION_FAILED` (HTTP 400).
- Frontend: shadcn components only; detail/edit views stay a right-side Sheet driven by the Zustand store.
- Nepali default text is machine-drafted; flag it in docs, do not block on review.
- Commit trailer on every commit: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Test infra for Api tests: `courier-postgres-1` (5434), `courier-redis-1`, `courier-rabbitmq-1`, `courier-mailhog` must be running. Do not start a second Postgres.

## File Structure

| File | Responsibility |
|---|---|
| `src/Modules/Notifications/Domain/NotificationLocales.cs` (new) | Supported/default locale constants, `Normalize` |
| `src/Modules/Notifications/Domain/NotificationTemplate.cs` | + `Locale` |
| `src/Modules/Notifications/Infrastructure/NotificationTemplateConfiguration.cs` | column + index |
| `src/Persistence/Migrations/*_AddNotificationTemplateLocale*` (new) | additive migration |
| `src/Modules/Notifications/Application/NotificationDefaultTemplates.cs` | + Nepali set, `TryGetDefault`, `IsKnownEvent` |
| `src/Modules/Notifications/Application/NotificationTemplateResolver.cs` (new) | pure 4-step fallback |
| `src/Modules/Notifications/Application/IOrganizationLocaleLookup.cs` (new) | port |
| `src/Api/OrganizationLocaleLookupAdapter.cs` (new) | adapter to Identity |
| `src/Modules/Notifications/Application/NotificationDispatchService.cs` | resolve locale + use resolver |
| `src/Modules/Notifications/Application/NotificationTemplateService.cs`, `NotificationTemplateDtos.cs` | locale in list/upsert/audit |
| `src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs` | `locale` in request |
| `src/web/src/features/admin/settings/notification-templates/*` | locale in types/schema/store/panel/sheet |
| `docs/INTERNATIONAL.md`, `docs/ROADMAP.md` | status |

---

### Task 1: Locale on the template entity, configuration and migration

**Files:**
- Create: `src/Modules/Notifications/Domain/NotificationLocales.cs`
- Modify: `src/Modules/Notifications/Domain/NotificationTemplate.cs`
- Modify: `src/Modules/Notifications/Infrastructure/NotificationTemplateConfiguration.cs`
- Create: `src/Persistence/Migrations/<timestamp>_AddNotificationTemplateLocale.cs` (+ Designer, snapshot update — generated)
- Test: `tests/Notifications.Tests/NotificationLocalesTests.cs`

**Interfaces:**
- Produces: `Courier.Notifications.Domain.NotificationLocales` with `const string Default = "en"`, `IReadOnlyList<string> Supported`, `bool IsSupported(string? locale)`, `string Normalize(string? locale)` (returns `Default` for null/unsupported, otherwise the lower-cased value). `NotificationTemplate.Locale` (`string`, initializer `NotificationLocales.Default`).

- [ ] **Step 1: Write the failing test** — `tests/Notifications.Tests/NotificationLocalesTests.cs`

```csharp
using Courier.Notifications.Domain;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationLocalesTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("ne", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ReportsSupportedLocalesOnly(string? locale, bool expected) =>
        Assert.Equal(expected, NotificationLocales.IsSupported(locale));

    [Theory]
    [InlineData("ne", "ne")]
    [InlineData("NE", "ne")]
    [InlineData("fr", "en")]
    [InlineData(null, "en")]
    public void Normalize_FallsBackToEnglishForMissingOrUnsupported(string? locale, string expected) =>
        Assert.Equal(expected, NotificationLocales.Normalize(locale));

    [Fact]
    public void NewTemplate_DefaultsToEnglish() =>
        Assert.Equal("en", new NotificationTemplate { EventType = "shipment.created", Body = "b" }.Locale);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Notifications.Tests --filter "FullyQualifiedName~NotificationLocalesTests" --nologo -v q`
Expected: build error, `NotificationLocales` does not exist.

- [ ] **Step 3: Implement**

`NotificationLocales.cs`:
```csharp
namespace Courier.Notifications.Domain;

// Mirrors Identity's OrganizationLocaleOptions.SupportedLocales without referencing Identity
// (Notifications must not take a project reference to it — see IOrganizationLocaleLookup).
public static class NotificationLocales
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> Supported = new[] { "en", "ne" };

    public static bool IsSupported(string? locale) => locale is not null && Supported.Contains(locale);

    public static string Normalize(string? locale)
    {
        var lowered = locale?.ToLowerInvariant();
        return IsSupported(lowered) ? lowered! : Default;
    }
}
```

In `NotificationTemplate.cs` add after `Channel`:
```csharp
    public string Locale { get; set; } = NotificationLocales.Default;
```

In `NotificationTemplateConfiguration.cs` add `builder.Property(t => t.Locale).HasMaxLength(10).IsRequired().HasDefaultValue(NotificationLocales.Default);` and replace the composite index with `builder.HasIndex(t => new { t.OrganizationId, t.EventType, t.Channel, t.Locale });`, updating the comment above it to say the lookup is per org/event/channel/locale.

- [ ] **Step 4: Generate the migration**

Run from `D:\courier`: `dotnet ef migrations add AddNotificationTemplateLocale --project src/Persistence --startup-project src/Api --output-dir Migrations`
(If the repo's earlier migrations were generated with a different project pair, match `20260921044911_AddFxRates`'s location `src/Persistence/Migrations`.) Open the generated file and confirm it adds a non-null `Locale` `character varying(10)` column with `defaultValue: "en"` and swaps the index. Confirm the snapshot diff is limited to these changes.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Notifications.Tests --nologo -v q`
Expected: PASS (existing tests unaffected; the new ones pass).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Notifications src/Persistence tests/Notifications.Tests
git commit -m "feat(notifications): add Locale to NotificationTemplate with additive migration"
```

---

### Task 2: Locale-keyed default templates (en + ne)

**Files:**
- Modify: `src/Modules/Notifications/Application/NotificationDefaultTemplates.cs`
- Test: `tests/Notifications.Tests/NotificationDefaultTemplatesTests.cs`

**Interfaces:**
- Consumes: `NotificationLocales` (Task 1).
- Produces on `NotificationDefaultTemplates`: existing `Email` (English, unchanged), new `EmailNe` (same keys), `bool IsKnownEvent(string eventType)`, `bool TryGetDefault(string locale, string eventType, out string body)` (locale-specific only, no fallback; unknown locale or event returns false).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.RegularExpressions;
using Courier.Notifications.Application;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationDefaultTemplatesTests
{
    private static readonly Regex Placeholder = new(@"\{[A-Za-z]+\}");

    [Fact]
    public void EnglishAndNepali_CoverTheSameEventTypes() =>
        Assert.Equal(
            NotificationDefaultTemplates.Email.Keys.OrderBy(k => k),
            NotificationDefaultTemplates.EmailNe.Keys.OrderBy(k => k));

    [Fact]
    public void EnglishAndNepali_UseTheSamePlaceholdersPerEvent()
    {
        foreach (var (eventType, english) in NotificationDefaultTemplates.Email)
        {
            var nepali = NotificationDefaultTemplates.EmailNe[eventType];
            Assert.Equal(
                Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v),
                Placeholder.Matches(nepali).Select(m => m.Value).OrderBy(v => v));
        }
    }

    [Fact]
    public void TryGetDefault_ReturnsLocaleSpecificBody()
    {
        Assert.True(NotificationDefaultTemplates.TryGetDefault("ne", "shipment.created", out var ne));
        Assert.True(NotificationDefaultTemplates.TryGetDefault("en", "shipment.created", out var en));
        Assert.NotEqual(en, ne);
        Assert.Contains("{Awb}", ne);
    }

    [Fact]
    public void TryGetDefault_UnknownLocaleOrEvent_ReturnsFalse()
    {
        Assert.False(NotificationDefaultTemplates.TryGetDefault("fr", "shipment.created", out _));
        Assert.False(NotificationDefaultTemplates.TryGetDefault("ne", "not.an.event", out _));
    }

    [Fact]
    public void IsKnownEvent_MatchesEnglishKeys()
    {
        Assert.True(NotificationDefaultTemplates.IsKnownEvent("user.invited"));
        Assert.False(NotificationDefaultTemplates.IsKnownEvent("nope"));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/Notifications.Tests --filter "FullyQualifiedName~NotificationDefaultTemplatesTests" --nologo -v q`. Expected: build error (`EmailNe` missing).

- [ ] **Step 3: Implement** — in `NotificationDefaultTemplates.cs`, keep `Email` exactly as is and add (update the class comment to mention locales and that the Nepali text is machine-drafted, pending native review):

```csharp
    // Machine-drafted Nepali; needs native-speaker review (docs/INTERNATIONAL.md).
    public static readonly IReadOnlyDictionary<string, string> EmailNe = new Dictionary<string, string>
    {
        ["shipment.created"] = "तपाईंको ढुवानी {Awb} सिर्जना गरिएको छ र प्रक्रियामा छ।",
        ["shipment.delivered"] = "तपाईंको ढुवानी {Awb} डेलिभर गरिएको छ।",
        ["shipment.ndr_created"] = "हामी ढुवानी {Awb} डेलिभर गर्न सकेनौं। हाम्रो टोलीले छिट्टै तपाईंलाई सम्पर्क गर्नेछ।",
        ["cod.collected"] = "ढुवानी {ShipmentId} को लागि रु. {CollectedAmount} COD रकम संकलन गरिएको छ।",
        ["user.invited"] = "तपाईंलाई Courier मा {OrganizationName} मा सामेल हुन आमन्त्रित गरिएको छ। पासवर्ड सेट गर्नुहोस्: {InviteUrl}"
    };

    public static bool IsKnownEvent(string eventType) => Email.ContainsKey(eventType);

    // Locale-specific lookup with NO fallback — NotificationTemplateResolver owns the fallback chain.
    public static bool TryGetDefault(string locale, string eventType, out string body)
    {
        var source = locale switch
        {
            "en" => Email,
            "ne" => EmailNe,
            _ => null
        };
        if (source is not null && source.TryGetValue(eventType, out var found))
        {
            body = found;
            return true;
        }
        body = string.Empty;
        return false;
    }
```

- [ ] **Step 4: Run tests** — same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Notifications/Application/NotificationDefaultTemplates.cs tests/Notifications.Tests/NotificationDefaultTemplatesTests.cs
git commit -m "feat(notifications): add Nepali default templates and locale-aware default lookup"
```

---

### Task 3: Pure template resolver (four-step fallback)

**Files:**
- Create: `src/Modules/Notifications/Application/NotificationTemplateResolver.cs`
- Test: `tests/Notifications.Tests/NotificationTemplateResolverTests.cs`

**Interfaces:**
- Consumes: `NotificationDefaultTemplates.TryGetDefault`, `NotificationLocales.Default` (Tasks 1–2).
- Produces: `static string? NotificationTemplateResolver.Resolve(string locale, string eventType, IReadOnlyDictionary<string, string> orgActiveBodiesByLocale)`. `orgActiveBodiesByLocale` maps locale → body for the org's **active** Email overrides for that event. Returns `null` only when the event type has no `en` default.

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.Notifications.Application;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationTemplateResolverTests
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();
    private const string Evt = "shipment.created";

    private static string Default(string locale)
    {
        NotificationDefaultTemplates.TryGetDefault(locale, Evt, out var body);
        return body;
    }

    [Fact]
    public void OrgOverrideForResolvedLocale_Wins() =>
        Assert.Equal("ne-override", NotificationTemplateResolver.Resolve("ne", Evt,
            new Dictionary<string, string> { ["ne"] = "ne-override", ["en"] = "en-override" }));

    [Fact]
    public void NoOrgOverride_UsesCompiledDefaultForLocale() =>
        Assert.Equal(Default("ne"), NotificationTemplateResolver.Resolve("ne", Evt, None));

    [Fact]
    public void CompiledLocaleDefault_BeatsOrgEnglishOverride() =>
        Assert.Equal(Default("ne"), NotificationTemplateResolver.Resolve("ne", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnsupportedLocale_FallsToOrgEnglishOverride() =>
        Assert.Equal("en-override", NotificationTemplateResolver.Resolve("fr", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnsupportedLocaleNoOverride_FallsToCompiledEnglish() =>
        Assert.Equal(Default("en"), NotificationTemplateResolver.Resolve("fr", Evt, None));

    [Fact]
    public void English_OrgOverrideBeatsCompiled() =>
        Assert.Equal("en-override", NotificationTemplateResolver.Resolve("en", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnknownEvent_ReturnsNull() =>
        Assert.Null(NotificationTemplateResolver.Resolve("en", "not.an.event", None));
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~NotificationTemplateResolverTests"`. Expected: build error.

- [ ] **Step 3: Implement**

```csharp
using Courier.Notifications.Domain;

namespace Courier.Notifications.Application;

// Spec §2 fallback: org override (locale) -> compiled default (locale) -> org override (en) ->
// compiled default (en). A locale-appropriate compiled body deliberately beats an org's English
// override so a Nepali tenant never silently gets English when a Nepali default exists.
public static class NotificationTemplateResolver
{
    public static string? Resolve(string locale, string eventType, IReadOnlyDictionary<string, string> orgActiveBodiesByLocale)
    {
        if (orgActiveBodiesByLocale.TryGetValue(locale, out var orgLocaleBody))
        {
            return orgLocaleBody;
        }
        if (NotificationDefaultTemplates.TryGetDefault(locale, eventType, out var defaultLocaleBody))
        {
            return defaultLocaleBody;
        }
        if (orgActiveBodiesByLocale.TryGetValue(NotificationLocales.Default, out var orgEnglishBody))
        {
            return orgEnglishBody;
        }
        return NotificationDefaultTemplates.TryGetDefault(NotificationLocales.Default, eventType, out var englishDefault)
            ? englishDefault
            : null;
    }
}
```

- [ ] **Step 4: Run tests.** Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Notifications/Application/NotificationTemplateResolver.cs tests/Notifications.Tests/NotificationTemplateResolverTests.cs
git commit -m "feat(notifications): add pure locale fallback resolver for template bodies"
```

---

### Task 4: Locale-aware dispatch, locale lookup port and Api adapter

**Files:**
- Create: `src/Modules/Notifications/Application/IOrganizationLocaleLookup.cs`
- Create: `src/Api/OrganizationLocaleLookupAdapter.cs`
- Modify: `src/Modules/Notifications/Application/NotificationDispatchService.cs`
- Modify: `src/Api/Program.cs` (register adapter next to line ~260 where `NotificationDispatchService` is registered)
- Modify + Test: `tests/Notifications.Tests/NotificationDispatchServiceTests.cs`

**Interfaces:**
- Consumes: `NotificationTemplateResolver.Resolve`, `NotificationLocales.Normalize`, `NotificationDefaultTemplates.IsKnownEvent`.
- Produces: `IOrganizationLocaleLookup { Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken ct = default); }` (returns a locale string; caller normalizes). `NotificationDispatchService` constructor becomes `(DbContext, IMerchantContactLookupService, IOrganizationLocaleLookup, IEnumerable<INotificationChannel>)`.

- [ ] **Step 1: Update existing tests and add failing tests** in `NotificationDispatchServiceTests.cs`

Add a fake near the other `file class` fakes:
```csharp
file class FakeOrganizationLocaleLookup : IOrganizationLocaleLookup
{
    public string Locale { get; set; } = "en";
    public Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Locale);
}
```
Change **every** `new NotificationDispatchService(dbContext, merchantLookup, channels)` in this file to pass `new FakeOrganizationLocaleLookup()` as the third argument (existing tests keep English behaviour). Then add:

```csharp
    [Fact]
    public async Task DispatchEventAsync_NepaliTenant_SendsNepaliDefault()
    {
        await using var dbContext = CreateContext();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup { Locale = "ne" },
            new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "AWB123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        NotificationDefaultTemplates.TryGetDefault("ne", "shipment.created", out var neBody);
        Assert.Single(emailChannel.Sent);
        Assert.Equal(neBody.Replace("{Awb}", "AWB123"), emailChannel.Sent[0].Body);
    }

    [Fact]
    public async Task DispatchEventAsync_NepaliTenantWithNepaliOverride_UsesOverride()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Locale = "ne", Body = "NE custom {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup { Locale = "ne" },
            new INotificationChannel[] { emailChannel });

        await service.DispatchEventAsync(organizationId, "shipment.created",
            System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "A1" }));

        Assert.Equal("NE custom A1", emailChannel.Sent.Single().Body);
    }

    [Fact]
    public async Task DispatchEventAsync_OtherOrganizationsOverride_IsNotUsed()
    {
        await using var dbContext = CreateContext();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created",
            Channel = NotificationChannel.Email, Locale = "en", Body = "LEAK {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup(),
            new INotificationChannel[] { emailChannel });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created",
            System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "A1" }));

        Assert.DoesNotContain("LEAK", emailChannel.Sent.Single().Body);
    }
```

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Notifications.Tests --nologo -v q`. Expected: build errors (`IOrganizationLocaleLookup`, constructor arity).

- [ ] **Step 3: Implement**

`IOrganizationLocaleLookup.cs`:
```csharp
namespace Courier.Notifications.Application;

// Implemented by Courier.Api (OrganizationLocaleLookupAdapter) against Identity's
// OrganizationSettingsService so Notifications never takes a project reference to Identity —
// same shape as Branches' IBranchPlanLimitProvider.
public interface IOrganizationLocaleLookup
{
    Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

`src/Api/OrganizationLocaleLookupAdapter.cs`:
```csharp
using Courier.Identity.Application;
using Courier.Notifications.Application;

namespace Courier.Api;

// Bridges Notifications.Application.IOrganizationLocaleLookup to Identity's
// OrganizationSettingsService.GetLocaleAsync (Guid-keyed, permission-free, ignores the ambient
// tenant filter — safe from NotificationsConsumer's no-tenant-context scope). The Api project is
// the one place that references both modules.
public class OrganizationLocaleLookupAdapter : IOrganizationLocaleLookup
{
    private readonly OrganizationSettingsService _settingsService;

    public OrganizationLocaleLookupAdapter(OrganizationSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        (await _settingsService.GetLocaleAsync(organizationId, cancellationToken)).DefaultLocale;
}
```
(Confirm `OrganizationLocaleDto`'s first property name is `DefaultLocale`, per `new OrganizationLocaleDto(settings.DefaultLocale, ...)`; and that `OrganizationSettingsService` is registered scoped — grep `Program.cs`.)

`Program.cs`: add `builder.Services.AddScoped<Courier.Notifications.Application.IOrganizationLocaleLookup, Courier.Api.OrganizationLocaleLookupAdapter>();` beside the `NotificationDispatchService` registration.

`NotificationDispatchService.cs`:
- Add field/ctor param `IOrganizationLocaleLookup _organizationLocaleLookup` (third parameter, before `channels`).
- Replace the `TryGetValue(...defaultBody)` guard with `if (!NotificationDefaultTemplates.IsKnownEvent(eventType)) { return; }` (keep the existing explanatory comment).
- Replace the single-template query and `Render(template ?? defaultBody, payload)` with:

```csharp
        var locale = NotificationLocales.Normalize(await _organizationLocaleLookup.GetDefaultLocaleAsync(organizationId, cancellationToken));

        // (keep the existing IgnoreQueryFilters() rationale comment above this query)
        var overrides = await _dbContext.Set<NotificationTemplate>().IgnoreQueryFilters()
            .Where(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == NotificationChannel.Email && t.IsActive)
            .Select(t => new { t.Locale, t.Body })
            .ToListAsync(cancellationToken);
        var bodiesByLocale = overrides
            .GroupBy(t => t.Locale)
            .ToDictionary(g => g.Key, g => g.First().Body);
        var body = Render(NotificationTemplateResolver.Resolve(locale, eventType, bodiesByLocale)!, payload);
```
(`Resolve` cannot return null here because `IsKnownEvent` already passed.)

- [ ] **Step 4: Run tests** — `dotnet test tests/Notifications.Tests --nologo -v q`, then `dotnet build Courier.slnx -v q --nologo`. Expected: PASS, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Notifications src/Api tests/Notifications.Tests
git commit -m "feat(notifications): render notifications in the tenant default locale"
```

---

### Task 5: Locale in the template service, DTO and endpoint

**Files:**
- Modify: `src/Modules/Notifications/Application/NotificationTemplateDtos.cs`
- Modify: `src/Modules/Notifications/Application/NotificationTemplateService.cs`
- Modify: `src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs`
- Modify + Test: `tests/Notifications.Tests/NotificationTemplateServiceTests.cs`

**Interfaces:**
- Consumes: `NotificationLocales`, `NotificationDefaultTemplates.IsKnownEvent`.
- Produces: `NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive, string Locale = "en")`; `UpsertAsync(Guid actingUserId, string eventType, NotificationChannel channel, string body, bool isActive, string? locale = null, CancellationToken cancellationToken = default)` — null locale means `en`; a non-null unsupported locale throws `NotificationValidationException`; `UpsertNotificationTemplateRequest(string EventType, NotificationChannel Channel, string Body, bool IsActive, string? Locale = null)`.

- [ ] **Step 1: Failing tests** — append to `NotificationTemplateServiceTests` (helpers `CreateContext`, `FixedTenantProvider`, `FakeAuditLogger` already exist in the file):

```csharp
    [Fact]
    public async Task UpsertAsync_UnsupportedLocale_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new FakeAuditLogger());

        await Assert.ThrowsAsync<NotificationValidationException>(() =>
            service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Body", true, "fr"));
    }

    [Fact]
    public async Task UpsertAsync_NoLocale_StoresEnglish()
    {
        await using var dbContext = CreateContext();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new FakeAuditLogger());

        var dto = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Body", true);

        Assert.Equal("en", dto.Locale);
    }

    [Fact]
    public async Task UpsertAsync_SameEventDifferentLocales_KeepsSeparateRows_AndUpdatesInPlace()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var audit = new FakeAuditLogger();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), audit);
        var user = Guid.NewGuid();

        await service.UpsertAsync(user, "shipment.created", NotificationChannel.Email, "EN one", true, "en");
        await service.UpsertAsync(user, "shipment.created", NotificationChannel.Email, "NE one", true, "ne");
        await service.UpsertAsync(user, "shipment.created", NotificationChannel.Email, "NE two", true, "ne");

        var rows = await service.ListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("NE two", rows.Single(r => r.Locale == "ne").Body);
        Assert.Equal("EN one", rows.Single(r => r.Locale == "en").Body);
        Assert.Contains("\"locale\":\"ne\"", audit.Entries.Last().After);
    }
```

- [ ] **Step 2: Run to verify failure** (`--filter FullyQualifiedName~NotificationTemplateServiceTests`). Expected: build error.

- [ ] **Step 3: Implement**

DTO: `public sealed record NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive, string Locale = NotificationLocales.Default);` (add `using Courier.Notifications.Domain;` already present).

Service:
- `ListAsync` select: `new NotificationTemplateDto(t.Id, t.EventType, t.Channel, t.Body, t.IsActive, t.Locale)`.
- `UpsertAsync` new signature (locale before `cancellationToken`). At the top replace the event check with `NotificationDefaultTemplates.IsKnownEvent(eventType)` and add:
```csharp
        if (locale is not null && !NotificationLocales.IsSupported(locale))
        {
            throw new NotificationValidationException($"Locale must be one of: {string.Join(", ", NotificationLocales.Supported)}.");
        }
        var resolvedLocale = locale ?? NotificationLocales.Default;
```
- The `existing` query adds `&& t.Locale == resolvedLocale`; the create branch sets `Locale = resolvedLocale`; audit `After` adds `locale = resolvedLocale`; the returned DTO passes `template.Locale`.
- Update the method's leading comment to say the natural key is (org, eventType, channel, locale).

Endpoint: add `string? Locale = null` to `UpsertNotificationTemplateRequest` and call `service.UpsertAsync(actingUserId, request.EventType, request.Channel, request.Body, request.IsActive, request.Locale, httpContext.RequestAborted)`.

Fix any other callers: `grep -rn "UpsertAsync(" src tests --include=*.cs | grep -i template`; existing tests that pass a `CancellationToken` positionally as the 6th argument must be updated to the named form `cancellationToken: ...`.

- [ ] **Step 4: Run** `dotnet test tests/Notifications.Tests --nologo -v q` and `dotnet build Courier.slnx -v q --nologo`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Notifications tests/Notifications.Tests
git commit -m "feat(notifications): add locale to template list/upsert API and audit"
```

---

### Task 6: Api-level tests (contract, isolation, end-to-end Nepali email)

**Files:**
- Modify: `tests/Api.Tests/NotificationTemplatesEndpointsTests.cs`
- Modify: `tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs`

**Interfaces:**
- Consumes: the `/api/v1/settings/notification-templates` contract from Task 5; `PATCH /api/v1/settings/organization/profile` body `OrganizationProfileDto` (`supportEmail, supportPhone, addressLine, logoUrl, timeZoneId, businessHours, defaultLocale, calendarPreference`); helpers already in each file (`RegisterOrgAsync`, `SeedBranchesAsync`, `CreateMerchantAsync`, `PlainShipmentRequest`, `PollMailhogForRecipientAsync`).

Infra must be up (see Global Constraints) before running.

- [ ] **Step 1: Write tests**

In `NotificationTemplatesEndpointsTests`, following that file's existing pattern (owner registers with `RegisterOrgAsync`, sets the bearer token on the client), add:
1. `Put_WithNepaliLocale_ReturnsLocaleAndListShowsBothLocales` — PUT `{ eventType: "shipment.created", channel: "Email", body: "EN {Awb}", isActive: true }` (no locale → `locale == "en"` in response) and PUT the same with `locale: "ne"`, body `"NE {Awb}"`; GET returns exactly two rows for `shipment.created` with locales `en` and `ne`.
2. `Put_WithUnsupportedLocale_Returns400` — `locale: "fr"` → `HttpStatusCode.BadRequest`, JSON `error.code == "NOTIFICATION_TEMPLATE_VALIDATION_FAILED"`.
3. `Get_DoesNotReturnAnotherOrganizationsLocaleTemplates` — org A upserts an `ne` template; a second registered org's GET contains no rows.
4. Existing tests keep passing unchanged (the field is optional).

In `NotificationsAndWebhooksEndToEndTests` add `ShipmentCreated_ForNepaliTenant_SendsNepaliEmail`: register org, set bearer, `PATCH /api/v1/settings/organization/profile` with `{ timeZoneId = "Asia/Kathmandu", defaultLocale = "ne" }` (`EnsureSuccessStatusCode`), then reproduce the body of `ShipmentCreated_SendsRealEmailToMerchantContactEmail` (seed branches, merchant, create shipment, poll MailHog). Assertions: the message is found; the raw body contains the AWB; and the raw body does **not** contain the English default fragment `"has been created and is being processed"`. Note the email is MIME `TextPart("plain")` with UTF-8, so the Nepali text may be transfer-encoded — do not assert on Nepali glyphs in the raw body; the positive Nepali-content assertion is covered by the dispatch unit test in Task 4.

- [ ] **Step 2: Run** `dotnet test tests/Api.Tests --nologo -v q --filter "FullyQualifiedName~NotificationTemplatesEndpointsTests|FullyQualifiedName~NotificationsAndWebhooksEndToEndTests"`. Expected: PASS, and this run also applies the new migration to the live DB (confirm it lists no errors).

- [ ] **Step 3: Verify migration backfill** with the shared container:
`docker exec courier-postgres-1 psql -U courier -d courier -c "select \"Locale\", count(*) from \"NotificationTemplates\" group by 1"`. Expected: only `en` rows plus any `ne` rows the tests created.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests
git commit -m "test(api): cover notification template locale contract, isolation and Nepali email"
```

---

### Task 7: Frontend — locale in the template panel and sheet

**Files:**
- Modify: `src/web/src/features/admin/settings/notification-templates/types.ts`
- Modify: `.../schema.ts` (no change needed unless locale becomes a form field — it does not; locale comes from the row)
- Modify: `.../store/notificationTemplatePanelStore.ts` and `notificationTemplatePanelStore.test.ts`
- Modify: `.../components/NotificationTemplatesPanel.tsx`
- Modify: `.../components/NotificationTemplateEditSheet.tsx`
- Create: `.../components/NotificationTemplatesPanel.test.tsx`

**Interfaces:**
- Produces: `type NotificationLocale = "en" | "ne"`; `NotificationTemplate.locale: NotificationLocale`; `UpsertNotificationTemplateInput.locale: NotificationLocale`; store `Panel = { mode: "closed" } | { mode: "edit"; eventType: string; channel: NotificationChannel; locale: NotificationLocale }`; `openEdit(eventType, channel, locale)`.

- [ ] **Step 1: Update the store test first** (`notificationTemplatePanelStore.test.ts`): change both `openEdit("shipment.created", "Email")` calls to `openEdit("shipment.created", "Email", "ne")` and the expected panel to `{ mode: "edit", eventType: "shipment.created", channel: "Email", locale: "ne" }`. Run `npx vitest run src/features/admin/settings/notification-templates` from `src/web`; expected FAIL (type/expectation mismatch).

- [ ] **Step 2: Implement store and types**

`types.ts`: add `export type NotificationLocale = "en" | "ne";`, add `locale: NotificationLocale;` to both `NotificationTemplate` and `UpsertNotificationTemplateInput`.

Store: `Panel` edit variant gains `locale: NotificationLocale`; `openEdit: (eventType, channel, locale) => set({ panel: { mode: "edit", eventType, channel, locale } })`; import the type.

- [ ] **Step 3: Panel** — in `NotificationTemplatesPanel.tsx`: add `const LOCALES: { code: NotificationLocale; label: string }[] = [{ code: "en", label: "English" }, { code: "ne", label: "नेपाली" }];`. Extend `Row` with `locale: NotificationLocale`. Build rows as `EVENT_TYPES.flatMap(eventType => CHANNELS.flatMap(channel => LOCALES.map(({ code }) => ({ eventType, channel, locale: code, override: data?.find(t => t.eventType === eventType && t.channel === channel && t.locale === code) ?? null }))))`. Add a column after "Event": `{ key: "locale", header: "Language", render: (r) => LOCALES.find(l => l.code === r.locale)!.label }`. Edit button: `onClick={() => openEdit(r.eventType, r.channel, r.locale)}` and `rowKey={(r) => \`${r.eventType}-${r.channel}-${r.locale}\`}`. Keep the existing status logic; for locale rows without an override the "Using default" badge stays correct (the server falls back).

- [ ] **Step 4: Sheet** — in `NotificationTemplateEditSheet.tsx`: the `existing` lookup adds `&& t.locale === panel.locale`; the mutation call passes `locale: panel.locale`; the title becomes `` `${panel.eventType} — ${panel.channel} — ${panel.locale === "ne" ? "नेपाली" : "English"}` ``.

- [ ] **Step 5: Panel test** — `NotificationTemplatesPanel.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NotificationTemplatesPanel } from "./NotificationTemplatesPanel";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";

vi.mock("@/features/admin/settings/notification-templates/hooks/useNotificationTemplates", () => ({
  useNotificationTemplates: () => ({
    isLoading: false,
    data: [
      { id: "1", eventType: "shipment.created", channel: "Email", locale: "ne", body: "NE custom {Awb}", isActive: true },
    ],
  }),
}));

function renderPanel() {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <NotificationTemplatesPanel />
    </QueryClientProvider>
  );
}

describe("NotificationTemplatesPanel", () => {
  beforeEach(() => {
    useNotificationTemplatePanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows one row per event and language, marking only the Nepali override as custom", () => {
    renderPanel();
    expect(screen.getAllByText("English").length).toBe(5);
    expect(screen.getAllByText("नेपाली").length).toBeGreaterThanOrEqual(5);
    expect(screen.getByText("NE custom {Awb}")).toBeInTheDocument();
    expect(screen.getAllByText("Custom").length).toBe(1);
  });

  it("opens the edit sheet for the clicked language", () => {
    renderPanel();
    fireEvent.click(screen.getAllByRole("button", { name: "Edit" })[1]);
    const panel = useNotificationTemplatePanelStore.getState().panel;
    expect(panel).toMatchObject({ mode: "edit", eventType: "shipment.created", locale: "ne" });
  });
});
```
If the edit sheet's own hooks (`useUpsertNotificationTemplate`) need extra providers, the `QueryClientProvider` above covers them. `getAllByText("नेपाली")` uses `>= 5` because the open sheet title may also render it.

- [ ] **Step 6: Run** from `src/web`: `npx vitest run src/features/admin/settings/notification-templates`, then the full suite `npx vitest run`, `npx tsc --noEmit` (only the pre-existing errors in `summary/route.test.ts` and marketing tests are acceptable), `npm run lint`, `npm run build`. Expected: all green apart from the known tsc errors.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/admin/settings/notification-templates
git commit -m "feat(web): edit notification templates per language"
```

---

### Task 8: Docs and full verification

**Files:**
- Modify: `docs/INTERNATIONAL.md`
- Modify: `docs/ROADMAP.md`

- [ ] **Step 1: Docs.** In `docs/INTERNATIONAL.md`: set the I1b status row to "Implemented — Nepali default bodies are machine-drafted and need native review"; add a "Notification locale (I1b)" section stating: locale = tenant `DefaultLocale`; the four-step fallback order; `NotificationTemplates.Locale` (default `en`, backfilled); API `locale` field (optional, 400 on unsupported); the admin panel shows one row per event and language; limitations (no per-recipient locale, `NotificationLog` does not record locale, admin panel itself not translated). In the I0 row, replace "still need a first run against Postgres" with "verified against Postgres on 2026-09-21 (full Api suite 290/290; four tests are flaky only under full-suite parallel load)". In `docs/ROADMAP.md`'s International paragraph, mark I0 verified and I1b implemented.

- [ ] **Step 2: Full backend verification.**
```
dotnet build Courier.slnx -v q --nologo
for p in SharedKernel Pricing Identity Notifications Api; do dotnet test tests/$p.Tests --no-build --nologo -v q; done
```
Expected: 0 build errors; every project passes. If any Api test fails, re-run it alone before judging (known parallel-load flakiness in `PricingEndpointsTests.RateCardVersioning_*`, `CodEndpointsTests.ConcurrentFinanceVerification_*`, `RidersEndpointsTests.GetRunBreadcrumbs_*`, `PublicTrackingEndpointTests.GetTrack_ExceedsConfiguredIpLimit_*`).

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "docs: document international I1b (notification-template locale)"
```
