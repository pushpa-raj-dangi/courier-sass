# Frontend Phase F12 — Rider Mobile-Web (PWA) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Frontend Phase F5 desktop-approximated rider self-service surface (`(staff)/my-deliveries`) with a real installable, offline-capable, camera-scanning mobile-web PWA at `/rider/*`.

**Architecture:** A new real Next.js path segment `app/rider/` (not a route group — PWA scope needs a real URL prefix) inside the existing `src/web` app, reusing the existing BFF/httpOnly-cookie session unchanged. Every rider write goes through an IndexedDB-backed outbox queue (`features/rider-app/sync/`) instead of a direct `clientFetch` call, drained on reconnect/foreground/30s-poll (Background Sync API as an Android-only bonus). A backend gap is closed first: `IdempotencyMiddleware` gains protection for the four rider action routes the queue replays, so a retried queue item can never double-apply.

**Tech Stack:** Next.js 14 App Router, `@serwist/next`/`serwist` (PWA/service worker), `idb` (IndexedDB), native `BarcodeDetector` API with a lazy-loaded `@zxing/browser` fallback, TanStack Query, Zustand, existing shadcn/ui component set.

**Spec:** `docs/superpowers/specs/2026-09-04-frontend-phase-f12-rider-mobile-web-pwa-design.md`

## Global Constraints

- PWA tooling is **Serwist** (`@serwist/next`, `serwist`) — not `next-pwa` (archived/unmaintained since Aug 2023).
- IndexedDB access goes through **`idb`** — not Dexie/RxDB (no general-purpose sync engine needed, just one outbox table).
- Camera scanning uses the native `BarcodeDetector` API first; `@zxing/browser` is **dynamically imported** only when `BarcodeDetector` is unavailable — never eagerly bundled.
- Every rider write (pickup scan, delivery fail, delivery complete) is enqueued through `features/rider-app/sync/` — never called via a direct `clientFetch`/`fetch` POST from a rider-app component.
- Idempotency keys are generated via the existing `generateIdempotencyKey()` (`src/web/src/lib/api/idempotencyKey.ts`) — do not reinvent.
- All shadcn/ui components only — no new raw HTML form primitives (project-standing convention).
- New rider-app frontend code lives entirely under `features/rider-app/` and `app/rider/`. Do **not** modify `features/riders/*`, `(staff)/my-deliveries`, or their tests until Task 13 (the dedicated retirement task) — this keeps every intermediate task's build/tests green without a half-migrated shared file.
- TDD throughout: failing test before implementation, every task.
- Deliberate scope narrowing from the spec (noted here so it isn't mistaken for a gap): today's task list stays read-only via the existing TanStack Query in-memory cache (already survives while the PWA tab/app stays open, which offline sync itself requires anyway per the spec's iOS foreground-only reality) — no separate IndexedDB read-cache is built. The spec's passing mention of "a small task-list read cache" is satisfied by this, not left undone.

---

### Task 1: Backend — Idempotency protection for rider action routes

**Files:**
- Modify: `src/Api/Middleware/IdempotencyMiddleware.cs`
- Test: `tests/Api.Tests/RiderIdempotencyTests.cs` (new)

**Interfaces:**
- Consumes: `Courier.SharedKernel.ClaimsPrincipalExtensions.GetUserId()`/`GetMerchantId()` (existing), `IIdempotencyStore` (existing).
- Produces: the four routes `POST /api/v1/riders/{id}/scan/pickup`, `POST /api/v1/riders/{id}/delivery-attempts/fail`, `POST /api/v1/riders/{id}/delivery-attempts/complete`, `POST /api/v1/riders/{id}/pod-uploads` now require an `Idempotency-Key` header, partitioned per-caller as `staff:{userId}:{idempotencyKey}` — Task 3's BFF routes rely on this.

- [ ] **Step 1: Write the failing tests**

Create `tests/Api.Tests/RiderIdempotencyTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Covers the F12 backend gap: IdempotencyMiddleware protected only merchant-JWT routes before
// this phase. A rider's offline-queued mutation can be replayed after a false-negative network
// failure, so the four rider action routes now require the same header — partitioned per staff
// user (`staff:{userId}`), not per merchant. Mirrors IdempotencyTests.cs's helper conventions;
// this file defines its own copies rather than sharing, matching that file's and
// RidersEndpointsTests.cs's established per-file-duplication convention in this codebase.
public class RiderIdempotencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public RiderIdempotencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"RiderIdem Org {suffix}", $"rider-idem-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid FromBranchId, Guid ToBranchId, Guid ServiceTypeId)> SeedBranchesAsync(HttpClient client)
    {
        var fromBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"FR-{Guid.NewGuid():N}"[..10]));
        var fromBranch = await fromBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var toBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"TO-{Guid.NewGuid():N}"[..10]));
        var toBranch = await toBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        await client.PutAsJsonAsync($"/api/v1/branches/{fromBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(originZone!.Id));
        await client.PutAsJsonAsync($"/api/v1/branches/{toBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(destZone!.Id));

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originZone.Id, destZone.Id, standard.Id, slab.Id, 100m));

        return (fromBranch.Id, toBranch.Id, standard.Id);
    }

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Books a shipment and walks it to AtDestinationBranch via the real pickup + manifest flow,
    // identical shape to RidersEndpointsTests.cs's BookAndMoveToDestinationAsync.
    private async Task<ShipmentResponse> BookAndMoveToDestinationAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup-noop-unused", null); // never sent, kept out — see next line for the real call
        return shipment; // placeholder overwritten below in each test via the real scan call, since scan is what's under test
    }

    [Fact]
    public async Task RiderScanPickup_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "scan-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));

        var response = await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RiderScanPickup_SameKeySameBody_ReplaysExactResponse_NoDuplicateTransition()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "scan-replay");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
        request1.Headers.Add("Idempotency-Key", "scan-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Without the fix, this second call would hit the real handler again — the shipment is
        // already PickedUp, so a genuine re-execution would either error or silently no-op
        // differently. With the fix it never reaches the handler at all: byte-identical replay.
        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
        request2.Headers.Add("Idempotency-Key", "scan-k1");
        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.Equal(body1, body2);

        var afterScan = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
    }

    [Fact]
    public async Task RiderDeliveryComplete_SameKeySameBody_ReplaysExactResponse_NoDuplicateCodCollection()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "complete-replay");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        var completeRequestBody = new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null);

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        request1.Headers.Add("Idempotency-Key", "complete-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var attempt1 = await response1.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();

        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        request2.Headers.Add("Idempotency-Key", "complete-k1");
        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var attempt2 = await response2.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();

        // Same DeliveryAttempt.Id on both responses proves the second call never re-executed the
        // real handler (which would either create a second attempt row or 400 on an
        // already-Delivered shipment) — it's a cached replay, not a second successful completion.
        Assert.Equal(attempt1!.Id, attempt2!.Id);

        var delivered = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);
    }

    [Fact]
    public async Task RiderDeliveryComplete_SameKeyDifferentBody_Returns409()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "complete-conflict");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null)) };
        request1.Headers.Add("Idempotency-Key", "complete-conflict-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "A Different Name", true, null, null, null, null)) };
        request2.Headers.Add("Idempotency-Key", "complete-conflict-k1");
        var response2 = await client.SendAsync(request2);

        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var payload = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    // Regression guard: the new RiderAction route classification must match exactly the 4 rider
    // action routes, not every /api/v1/riders/** route. Dispatcher-facing rider routes (create,
    // assign-task) must stay exempt, same as every other staff route.
    [Fact]
    public async Task OtherRiderRoutes_CreateAndAssignTask_RemainExemptFromIdempotency()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "other-routes");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);

        var suffix = Guid.NewGuid().ToString("N");
        var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
    }

    // Closes coverage on the other two of the four newly-protected routes — the replay/conflict
    // behavior itself is already proven generically by the scan-pickup and delivery-complete
    // tests above (shared middleware code path), but each route's own regex match still needs a
    // direct check, since a typo in any one of the four patterns would otherwise go undetected.
    [Fact]
    public async Task RiderDeliveryFail_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "fail-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        var response = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail",
            new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Retry tomorrow"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RiderPodUpload_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "pod-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var deliveryRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.DeliveryRider);

        var signatureContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        signatureContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await client.PostAsync($"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature", signatureContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter RiderIdempotencyTests`
Expected: `RiderScanPickup_NoIdempotencyKeyHeader_Returns400` FAILs (currently returns 200, not 400 — no
enforcement exists yet); the two replay tests FAIL differently (second call currently re-executes
the real handler, producing either a different body or a 400 invalid-state error instead of an
identical 200); the conflict test FAILs (currently both calls just succeed, no 409).
`OtherRiderRoutes_...` PASSes already (nothing to fix there — it's the regression guard).

- [ ] **Step 3: Implement the middleware change**

Replace the full contents of `src/Api/Middleware/IdempotencyMiddleware.cs` with:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Courier.SharedKernel;

namespace Courier.Api.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Regex ShipmentCancelPattern = new(@"^/api/v1/shipments/[0-9a-fA-F-]{36}/cancel$", RegexOptions.Compiled);
    private static readonly Regex RiderScanPickupPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/scan/pickup$", RegexOptions.Compiled);
    private static readonly Regex RiderDeliveryFailPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/delivery-attempts/fail$", RegexOptions.Compiled);
    private static readonly Regex RiderDeliveryCompletePattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/delivery-attempts/complete$", RegexOptions.Compiled);
    private static readonly Regex RiderPodUploadsPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/pod-uploads$", RegexOptions.Compiled);

    private enum RouteKind { None, MerchantWrite, RiderAction }

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore idempotencyStore)
    {
        var routeKind = ClassifyRoute(context.Request);
        if (routeKind == RouteKind.None)
        {
            await _next(context);
            return;
        }

        string partitionKey;
        if (routeKind == RouteKind.MerchantWrite)
        {
            var merchantId = context.User.GetMerchantId();
            if (merchantId is null)
            {
                // Staff-JWT (or unauthenticated) callers on these same routes are completely
                // untouched — unchanged from the original merchant-only design, and the reason
                // every existing ShipmentsEndpointsTests/PickupsEndpointsTests case (all
                // staff-JWT-driven, none sending Idempotency-Key) still passes unmodified.
                await _next(context);
                return;
            }
            partitionKey = merchantId.Value.ToString();
        }
        else
        {
            var userId = context.User.GetUserId();
            if (userId is null)
            {
                await _next(context);
                return;
            }
            partitionKey = $"staff:{userId}";
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader) || string.IsNullOrWhiteSpace(keyHeader))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required for this request.");
            return;
        }
        var idempotencyKey = keyHeader.ToString();

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;
        var bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));

        var storeKey = $"{partitionKey}:{idempotencyKey}";
        var existing = await idempotencyStore.GetAsync(storeKey, context.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestBodyHash != bodyHash)
            {
                await WriteErrorAsync(context, StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used with a different request body.");
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(existing.ResponseBody);
            return;
        }

        var originalBody = context.Response.Body;
        await using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        capturedBody.Position = 0;
        var responseText = await new StreamReader(capturedBody).ReadToEndAsync();
        capturedBody.Position = 0;
        await capturedBody.CopyToAsync(originalBody);

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await idempotencyStore.StoreAsync(storeKey, new IdempotencyRecord(bodyHash, context.Response.StatusCode, responseText), TimeSpan.FromHours(24), context.RequestAborted);
        }
    }

    private static RouteKind ClassifyRoute(HttpRequest request)
    {
        if (request.Method != HttpMethods.Post)
        {
            return RouteKind.None;
        }
        var path = request.Path.Value ?? string.Empty;
        if (path == "/api/v1/shipments" || path == "/api/v1/pickups" || ShipmentCancelPattern.IsMatch(path))
        {
            return RouteKind.MerchantWrite;
        }
        if (RiderScanPickupPattern.IsMatch(path) || RiderDeliveryFailPattern.IsMatch(path) ||
            RiderDeliveryCompletePattern.IsMatch(path) || RiderPodUploadsPattern.IsMatch(path))
        {
            return RouteKind.RiderAction;
        }
        return RouteKind.None;
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { code, message } }));
    }
}
```

Also delete the unused `BookAndMoveToDestinationAsync` placeholder helper from Step 1's test file
(it was never actually called by any test above — each test inlines its own scan call so it can
attach its own `Idempotency-Key` header). Remove that method from `RiderIdempotencyTests.cs`
before running tests.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "RiderIdempotencyTests|IdempotencyTests|RidersEndpointsTests"`
Expected: PASS — all new `RiderIdempotencyTests`, all pre-existing `IdempotencyTests` (merchant
path unchanged), and all pre-existing `RidersEndpointsTests` (none send an `Idempotency-Key`
header today; verify they still don't need to, since only staff-JWT callers *lacking* `userId`
would be exempt, but every staff JWT does carry a user id — check this doesn't newly break
`RidersEndpointsTests.cs`: it currently calls the four rider routes without an `Idempotency-Key`
header, so those calls now correctly 400. This is expected — Task 1b below fixes it.)

- [ ] **Step 4b: Fix `RidersEndpointsTests.cs` to send the now-required header**

`RidersEndpointsTests.cs` predates this phase and calls the four rider routes via plain
`PostAsJsonAsync` (no `Idempotency-Key`). Update every such call site in
`tests/Api.Tests/RidersEndpointsTests.cs` to use the `HttpRequestMessage` + header pattern instead,
e.g. replace:

```csharp
var scanResponse = await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
```

with:

```csharp
using var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
{ Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
var scanResponse = await client.SendAsync(scanRequest);
```

Apply the same transformation to every `.../scan/pickup`, `.../delivery-attempts/fail`, and
`.../delivery-attempts/complete` call in that file (search the file for those three path fragments
— there are calls in `BookAndMoveToDestinationAsync`, `FullSuccessPath_PickupToDelivered_...`, and
`FullFailurePath_OutForDeliveryToNdrCreated_...`), each with its own freshly generated key.

The one `.../pod-uploads` call (in `FullSuccessPath_PickupToDelivered_...`) uses `PostAsync` with a
raw `ByteArrayContent`, not `PostAsJsonAsync`, so its header needs adding directly on that existing
`HttpContent` via its own request instead — replace:

```csharp
var uploadResponse = await client.PostAsync($"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature", signatureContent);
```

with:

```csharp
using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature")
{ Content = signatureContent };
uploadRequest.Headers.Add("Idempotency-Key", $"pod-{Guid.NewGuid():N}");
var uploadResponse = await client.SendAsync(uploadRequest);
```

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS, no regressions anywhere.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Middleware/IdempotencyMiddleware.cs tests/Api.Tests/RiderIdempotencyTests.cs tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(api): extend idempotency protection to rider action routes"
```

---

### Task 2: Frontend — Add PWA/offline/scanning dependencies

**Files:**
- Modify: `src/web/package.json`

**Interfaces:**
- Produces: `idb`, `@serwist/next`, `serwist` (runtime + build-time PWA), `@zxing/browser` (lazy-loaded barcode fallback), `fake-indexeddb` (devDependency, test-only) available to every later task in this plan.

- [ ] **Step 1: Add the dependencies**

```bash
cd src/web
npm install idb @serwist/next serwist @zxing/browser
npm install -D fake-indexeddb
```

- [ ] **Step 2: Verify the existing build and test suite are unaffected**

Run: `npm run build && npm test`
Expected: PASS — adding dependencies alone changes no behavior.

- [ ] **Step 3: Commit**

```bash
git add package.json package-lock.json
git commit -m "chore(web): add PWA, IndexedDB, and barcode-scanning dependencies for Frontend Phase F12"
```

---

### Task 3: Frontend — Forward Idempotency-Key header through rider BFF routes

**Files:**
- Modify: `src/web/src/app/api/riders/[id]/scan/pickup/route.ts`
- Modify: `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.ts`
- Modify: `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.ts`
- Modify: `src/web/src/app/api/riders/[id]/pod-uploads/route.ts`
- Test: `src/web/src/app/api/riders/[id]/scan/pickup/route.test.ts` (extend existing)
- Test: `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.test.ts` (new)
- Test: `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.test.ts` (extend existing)
- Test: `src/web/src/app/api/riders/[id]/pod-uploads/route.test.ts` (extend existing)

**Interfaces:**
- Consumes: `apiFetch` (`@/lib/auth/apiFetch`, unchanged).
- Produces: all four routes now forward an inbound `Idempotency-Key` request header to the backend call — Task 5's sync manager relies on this to make Task 1's backend enforcement actually take effect end-to-end.

This task modifies `features/riders`-adjacent BFF route files, which is an exception to the
Global Constraints' "don't touch `features/riders/*`" rule — these are shared plumbing
(`app/api/riders/**`) used by both the old desktop page (until Task 13) and the new rider-app
hooks (Task 6), and forwarding an *absent* header is a no-op, so this change is safe for the old
page too (it doesn't send the header yet, and still won't need to — it's retired before Task 1's
backend requirement would ever be hit by it, since Task 13 runs before this phase is called done;
until then, the old page's calls will 400, which is expected and acceptable given Task 13 is part
of this same plan and always runs before the phase ships).

- [ ] **Step 1: Write the failing test for the pickup-scan route**

Add to `src/web/src/app/api/riders/[id]/scan/pickup/route.test.ts`:

```ts
  it("forwards the client's Idempotency-Key header to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "PickedUp" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/scan/pickup", {
      method: "POST",
      headers: { "Idempotency-Key": "key-123" },
      body: JSON.stringify({ awb: "AWB001" }),
    });
    await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/scan/pickup",
      expect.objectContaining({ headers: expect.objectContaining({ "Idempotency-Key": "key-123" }) })
    );
  });
```

Write the equivalent test into the other three test files (`delivery-attempts/fail`,
`delivery-attempts/complete`, `pod-uploads`) — same shape, adjusted URL and body/content-type
per that route's existing test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- riders`
Expected: FAIL — none of the four routes currently read or forward the header.

- [ ] **Step 3: Implement the forwarding in all four routes**

`src/web/src/app/api/riders/[id]/scan/pickup/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  const idempotencyKey = request.headers.get("Idempotency-Key");
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/scan/pickup`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
      },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Apply the identical `idempotencyKey` read + conditional-header-spread pattern to
`delivery-attempts/fail/route.ts` and `delivery-attempts/complete/route.ts` (both already have the
exact same JSON-body shape as pickup-scan). For `pod-uploads/route.ts`, add it alongside the
existing `contentType` header:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const { searchParams } = new URL(request.url);
  const kind = searchParams.get("kind") ?? "";
  const contentType = request.headers.get("content-type") ?? "application/octet-stream";
  const idempotencyKey = request.headers.get("Idempotency-Key");
  const bodyBuffer = await request.arrayBuffer();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/pod-uploads?kind=${kind}`, {
      method: "POST",
      headers: {
        "Content-Type": contentType,
        ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
      },
      body: bodyBuffer,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- riders`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/riders
git commit -m "feat(web): forward Idempotency-Key header through rider action BFF routes"
```

---

### Task 4: Frontend — Outbox IndexedDB storage

**Files:**
- Create: `src/web/src/features/rider-app/sync/types.ts`
- Create: `src/web/src/features/rider-app/sync/outboxDb.ts`
- Test: `src/web/src/features/rider-app/sync/outboxDb.test.ts`

**Interfaces:**
- Consumes: `idb` (`openDB`), `generateIdempotencyKey` (`@/lib/api/idempotencyKey`).
- Produces (used by Task 5): `QueuedAction` (discriminated union), `OutboxItem { id, action, idempotencyKey, createdAt, status: "pending"|"syncing"|"stuck", lastError? }`, `addOutboxItem(action: QueuedAction): Promise<OutboxItem>`, `listOutboxItems(): Promise<OutboxItem[]>` (ascending `createdAt`), `updateOutboxItem(id: string, patch: Partial<OutboxItem>): Promise<void>`, `removeOutboxItem(id: string): Promise<void>`, `resetOutboxDbForTests(): void`.

- [ ] **Step 1: Define the queue's action types**

Create `src/web/src/features/rider-app/sync/types.ts`:

```ts
export interface RecordFailedAttemptFields {
  shipmentId: string;
  reason:
    | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
    | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";
  notes?: string;
  latitude?: number | null;
  longitude?: number | null;
  customerContacted: boolean;
  nextAction?: string;
}

export type QueuedAction =
  | { kind: "pickup-scan"; riderId: string; shipmentId: string; awb: string }
  | { kind: "delivery-fail"; riderId: string; shipmentId: string; input: RecordFailedAttemptFields }
  | {
      kind: "delivery-complete";
      riderId: string;
      shipmentId: string;
      recipientName: string;
      otpVerified: boolean;
      latitude: number | null;
      longitude: number | null;
      collectedCodAmount: number | null;
      signatureBlob: Blob | null;
      photoBlob: Blob | null;
      signatureStorageKey: string | null;
      photoStorageKey: string | null;
    };

export type OutboxStatus = "pending" | "syncing" | "stuck";

export interface OutboxItem {
  id: string;
  action: QueuedAction;
  idempotencyKey: string;
  createdAt: number;
  status: OutboxStatus;
  lastError?: string;
}
```

- [ ] **Step 2: Write the failing tests**

Create `src/web/src/features/rider-app/sync/outboxDb.test.ts`:

```ts
import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it } from "vitest";
import {
  addOutboxItem,
  listOutboxItems,
  removeOutboxItem,
  resetOutboxDbForTests,
  updateOutboxItem,
} from "./outboxDb";
import type { QueuedAction } from "./types";

const pickupAction: QueuedAction = { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" };
const failAction: QueuedAction = {
  kind: "delivery-fail",
  riderId: "r1",
  shipmentId: "s2",
  input: { shipmentId: "s2", reason: "CustomerUnavailable", customerContacted: true },
};

describe("outboxDb", () => {
  beforeEach(async () => {
    resetOutboxDbForTests();
  });

  it("adds an item with a generated id, idempotency key, timestamp, and pending status", async () => {
    const item = await addOutboxItem(pickupAction);

    expect(item.id).toBeTruthy();
    expect(item.idempotencyKey).toBeTruthy();
    expect(item.status).toBe("pending");
    expect(item.action).toEqual(pickupAction);
  });

  it("lists items ordered by createdAt ascending (FIFO)", async () => {
    const first = await addOutboxItem(pickupAction);
    const second = await addOutboxItem(failAction);

    const items = await listOutboxItems();

    expect(items.map((i) => i.id)).toEqual([first.id, second.id]);
  });

  it("updates an item in place", async () => {
    const item = await addOutboxItem(pickupAction);

    await updateOutboxItem(item.id, { status: "stuck", lastError: "boom" });

    const [updated] = await listOutboxItems();
    expect(updated.status).toBe("stuck");
    expect(updated.lastError).toBe("boom");
  });

  it("removes an item", async () => {
    const item = await addOutboxItem(pickupAction);

    await removeOutboxItem(item.id);

    expect(await listOutboxItems()).toHaveLength(0);
  });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `npm test -- outboxDb`
Expected: FAIL with "Cannot find module './outboxDb'".

- [ ] **Step 4: Implement `outboxDb.ts`**

Create `src/web/src/features/rider-app/sync/outboxDb.ts`:

```ts
import { openDB, type DBSchema, type IDBPDatabase } from "idb";
import { generateIdempotencyKey } from "@/lib/api/idempotencyKey";
import type { OutboxItem, QueuedAction } from "./types";

interface OutboxDBSchema extends DBSchema {
  outbox: {
    key: string;
    value: OutboxItem;
    indexes: { "by-createdAt": number };
  };
}

const DB_NAME = "courier-rider-outbox";
const DB_VERSION = 1;

let dbPromise: Promise<IDBPDatabase<OutboxDBSchema>> | null = null;

function getDb(): Promise<IDBPDatabase<OutboxDBSchema>> {
  if (!dbPromise) {
    dbPromise = openDB<OutboxDBSchema>(DB_NAME, DB_VERSION, {
      upgrade(db) {
        const store = db.createObjectStore("outbox", { keyPath: "id" });
        store.createIndex("by-createdAt", "createdAt");
      },
    });
  }
  return dbPromise;
}

export async function addOutboxItem(action: QueuedAction): Promise<OutboxItem> {
  const item: OutboxItem = {
    id: crypto.randomUUID(),
    action,
    idempotencyKey: generateIdempotencyKey(),
    createdAt: Date.now(),
    status: "pending",
  };
  const db = await getDb();
  await db.put("outbox", item);
  return item;
}

export async function listOutboxItems(): Promise<OutboxItem[]> {
  const db = await getDb();
  return db.getAllFromIndex("outbox", "by-createdAt");
}

export async function updateOutboxItem(id: string, patch: Partial<OutboxItem>): Promise<void> {
  const db = await getDb();
  const existing = await db.get("outbox", id);
  if (!existing) return;
  await db.put("outbox", { ...existing, ...patch });
}

export async function removeOutboxItem(id: string): Promise<void> {
  const db = await getDb();
  await db.delete("outbox", id);
}

// Test-only: forces the next getDb() call to reopen a fresh connection against
// fake-indexeddb's per-test-reset global, so state doesn't leak between test cases.
export function resetOutboxDbForTests(): void {
  dbPromise = null;
  indexedDB.deleteDatabase(DB_NAME);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npm test -- outboxDb`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/rider-app/sync/types.ts src/web/src/features/rider-app/sync/outboxDb.ts src/web/src/features/rider-app/sync/outboxDb.test.ts
git commit -m "feat(web): add IndexedDB-backed outbox storage for the rider app's offline queue"
```

---

### Task 5: Frontend — Sync manager (replay, drain, triggers, stuck items)

**Files:**
- Create: `src/web/src/features/rider-app/sync/errors.ts`
- Create: `src/web/src/features/rider-app/sync/outboxStatusStore.ts`
- Create: `src/web/src/features/rider-app/sync/syncManager.ts`
- Create: `src/web/src/features/rider-app/sync/useOutboxStatus.ts`
- Test: `src/web/src/features/rider-app/sync/syncManager.test.ts`

**Interfaces:**
- Consumes: Task 4's `listOutboxItems`/`updateOutboxItem`/`removeOutboxItem`, global `fetch`.
- Produces (used by Task 6 and Task 8): `drainOutbox(): Promise<{ synced: string[]; stuck: string[] }>`, `startSyncManager(): () => void`, `useOutboxStatus(): { pendingCount: number; stuckItems: OutboxItem[]; lastSyncedAt: number | null; refresh: () => Promise<void> }`.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/rider-app/sync/syncManager.test.ts`:

```ts
import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { addOutboxItem, listOutboxItems, resetOutboxDbForTests } from "./outboxDb";
import { drainOutbox } from "./syncManager";
import type { QueuedAction } from "./types";

const pickupAction: QueuedAction = { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" };
const failAction: QueuedAction = {
  kind: "delivery-fail",
  riderId: "r1",
  shipmentId: "s2",
  input: { shipmentId: "s2", reason: "CustomerUnavailable", customerContacted: true },
};
const completeAction: QueuedAction = {
  kind: "delivery-complete",
  riderId: "r1",
  shipmentId: "s3",
  recipientName: "Recipient One",
  otpVerified: true,
  latitude: null,
  longitude: null,
  collectedCodAmount: null,
  signatureBlob: new Blob(["sig"], { type: "image/png" }),
  photoBlob: null,
  signatureStorageKey: null,
  photoStorageKey: null,
};

describe("drainOutbox", () => {
  beforeEach(() => {
    resetOutboxDbForTests();
    vi.restoreAllMocks();
  });

  it("replays a pickup-scan item and removes it on success", async () => {
    await addOutboxItem(pickupAction);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) }));

    const result = await drainOutbox();

    expect(result.synced).toHaveLength(1);
    expect(await listOutboxItems()).toHaveLength(0);
    expect(fetch).toHaveBeenCalledWith(
      "/api/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST", headers: expect.objectContaining({ "Idempotency-Key": expect.any(String) }) })
    );
  });

  it("uploads signature/photo blobs before calling delivery-attempts/complete, and persists the storage key between attempts", async () => {
    await addOutboxItem(completeAction);
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ storageKey: "sig-key-1" }) }) // pod-upload
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ id: "a1", succeeded: true }) }); // complete
    vi.stubGlobal("fetch", fetchMock);

    await drainOutbox();

    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/riders/r1/pod-uploads?kind=Signature", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ body: expect.stringContaining("sig-key-1") })
    );
  });

  it("marks a 4xx failure as stuck and does not retry it on a later drain", async () => {
    await addOutboxItem(failAction);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 400, text: async () => "invalid state" }));

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(1);
    const [item] = await listOutboxItems();
    expect(item.status).toBe("stuck");
    expect(item.lastError).toContain("invalid state");

    vi.mocked(fetch).mockClear();
    await drainOutbox();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("leaves a network-failure item pending (not stuck) so a later drain retries it", async () => {
    await addOutboxItem(pickupAction);
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(0);
    expect(result.synced).toHaveLength(0);
    const [item] = await listOutboxItems();
    expect(item.status).toBe("pending");
  });

  it("blocks later items for the same shipment once one goes stuck, but still drains a different shipment's item", async () => {
    await addOutboxItem({ kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" });
    await addOutboxItem({
      kind: "delivery-fail",
      riderId: "r1",
      shipmentId: "s1",
      input: { shipmentId: "s1", reason: "CustomerUnavailable", customerContacted: true },
    });
    await addOutboxItem({ kind: "pickup-scan", riderId: "r1", shipmentId: "s-other", awb: "AWB002" });

    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
        const body = typeof init?.body === "string" ? init.body : "";
        return body.includes("AWB001")
          ? Promise.resolve({ ok: false, status: 400, text: async () => "wrong state" })
          : Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
      })
    );

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(1); // the first s1 item
    expect(result.synced).toHaveLength(1); // the s-other item, drained despite s1 being blocked
    const remaining = await listOutboxItems();
    expect(remaining).toHaveLength(2); // the stuck s1 item + the still-queued second s1 item
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- syncManager`
Expected: FAIL with "Cannot find module './syncManager'".

- [ ] **Step 3: Implement the error type, status store, and sync manager**

Create `src/web/src/features/rider-app/sync/errors.ts`:

```ts
export class QueuedRequestError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "QueuedRequestError";
  }
}

export function isNonRetryableError(error: unknown): boolean {
  return error instanceof QueuedRequestError && error.status >= 400 && error.status < 500;
}
```

Create `src/web/src/features/rider-app/sync/outboxStatusStore.ts`:

```ts
import { create } from "zustand";
import { listOutboxItems } from "./outboxDb";
import type { OutboxItem } from "./types";

interface OutboxStatusState {
  pendingCount: number;
  stuckItems: OutboxItem[];
  lastSyncedAt: number | null;
  refresh: () => Promise<void>;
  recordSync: () => void;
}

export const useOutboxStatusStore = create<OutboxStatusState>((set) => ({
  pendingCount: 0,
  stuckItems: [],
  lastSyncedAt: null,
  refresh: async () => {
    const items = await listOutboxItems();
    set({
      pendingCount: items.filter((i) => i.status !== "stuck").length,
      stuckItems: items.filter((i) => i.status === "stuck"),
    });
  },
  recordSync: () => set({ lastSyncedAt: Date.now() }),
}));
```

Create `src/web/src/features/rider-app/sync/syncManager.ts`:

```ts
import { listOutboxItems, removeOutboxItem, updateOutboxItem } from "./outboxDb";
import { isNonRetryableError, QueuedRequestError } from "./errors";
import { useOutboxStatusStore } from "./outboxStatusStore";
import type { OutboxItem } from "./types";

async function syncFetch<T>(url: string, init: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new QueuedRequestError(response.status, text || `Request to ${url} failed with ${response.status}`);
  }
  return response.json();
}

async function replayAction(item: OutboxItem): Promise<void> {
  const { action, idempotencyKey } = item;
  const jsonHeaders = { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey };

  switch (action.kind) {
    case "pickup-scan": {
      await syncFetch(`/api/riders/${action.riderId}/scan/pickup`, {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify({ awb: action.awb }),
      });
      return;
    }
    case "delivery-fail": {
      await syncFetch(`/api/riders/${action.riderId}/delivery-attempts/fail`, {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify(action.input),
      });
      return;
    }
    case "delivery-complete": {
      // Local vars, not repeated type-narrowing through the QueuedAction union — each starts
      // from whatever the action already carries (non-null on a retry that already uploaded one
      // of the two files) and is persisted back onto the stored item after each successful
      // upload, so a later retry never re-uploads a file it already has a key for.
      let signatureStorageKey = action.signatureStorageKey;
      let photoStorageKey = action.photoStorageKey;

      if (action.signatureBlob && !signatureStorageKey) {
        const result = await syncFetch<{ storageKey: string }>(
          `/api/riders/${action.riderId}/pod-uploads?kind=Signature`,
          { method: "POST", headers: { "Content-Type": action.signatureBlob.type || "application/octet-stream" }, body: action.signatureBlob }
        );
        signatureStorageKey = result.storageKey;
        await updateOutboxItem(item.id, { action: { ...action, signatureStorageKey } });
      }
      if (action.photoBlob && !photoStorageKey) {
        const result = await syncFetch<{ storageKey: string }>(
          `/api/riders/${action.riderId}/pod-uploads?kind=Photo`,
          { method: "POST", headers: { "Content-Type": action.photoBlob.type || "application/octet-stream" }, body: action.photoBlob }
        );
        photoStorageKey = result.storageKey;
        await updateOutboxItem(item.id, { action: { ...action, signatureStorageKey, photoStorageKey } });
      }

      await syncFetch(`/api/riders/${action.riderId}/delivery-attempts/complete`, {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify({
          shipmentId: action.shipmentId,
          recipientName: action.recipientName,
          otpVerified: action.otpVerified,
          signatureStorageKey,
          photoStorageKey,
          latitude: action.latitude,
          longitude: action.longitude,
          collectedCodAmount: action.collectedCodAmount,
        }),
      });
      return;
    }
  }
}

export async function drainOutbox(): Promise<{ synced: string[]; stuck: string[] }> {
  const items = await listOutboxItems();
  const synced: string[] = [];
  const stuck: string[] = [];
  const blockedShipmentIds = new Set<string>();

  for (const item of items) {
    const shipmentId = item.action.shipmentId;
    if (item.status === "stuck") {
      blockedShipmentIds.add(shipmentId);
      continue;
    }
    if (blockedShipmentIds.has(shipmentId)) {
      continue;
    }

    await updateOutboxItem(item.id, { status: "syncing" });
    try {
      await replayAction(item);
      await removeOutboxItem(item.id);
      synced.push(item.id);
    } catch (error) {
      if (isNonRetryableError(error)) {
        const message = error instanceof Error ? error.message : "Unknown error";
        await updateOutboxItem(item.id, { status: "stuck", lastError: message });
        stuck.push(item.id);
        blockedShipmentIds.add(shipmentId);
      } else {
        await updateOutboxItem(item.id, { status: "pending" });
        break; // likely offline again — stop hammering the rest of the queue
      }
    }
  }

  await useOutboxStatusStore.getState().refresh();
  useOutboxStatusStore.getState().recordSync();
  return { synced, stuck };
}

export function startSyncManager(): () => void {
  const handleOnline = () => { void drainOutbox(); };
  const handleVisibility = () => {
    if (document.visibilityState === "visible") void drainOutbox();
  };

  window.addEventListener("online", handleOnline);
  document.addEventListener("visibilitychange", handleVisibility);
  const pollId = window.setInterval(() => { void drainOutbox(); }, 30_000);
  void drainOutbox();

  return () => {
    window.removeEventListener("online", handleOnline);
    document.removeEventListener("visibilitychange", handleVisibility);
    window.clearInterval(pollId);
  };
}
```

Create `src/web/src/features/rider-app/sync/useOutboxStatus.ts`:

```ts
"use client";

import { useEffect } from "react";
import { useOutboxStatusStore } from "./outboxStatusStore";

export function useOutboxStatus() {
  const pendingCount = useOutboxStatusStore((s) => s.pendingCount);
  const stuckItems = useOutboxStatusStore((s) => s.stuckItems);
  const lastSyncedAt = useOutboxStatusStore((s) => s.lastSyncedAt);
  const refresh = useOutboxStatusStore((s) => s.refresh);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { pendingCount, stuckItems, lastSyncedAt, refresh };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- syncManager`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/rider-app/sync/errors.ts src/web/src/features/rider-app/sync/outboxStatusStore.ts src/web/src/features/rider-app/sync/syncManager.ts src/web/src/features/rider-app/sync/useOutboxStatus.ts src/web/src/features/rider-app/sync/syncManager.test.ts
git commit -m "feat(web): add sync manager draining the rider app's offline queue"
```

---

### Task 6: Frontend — Outbox-aware rider mutation hooks

**Files:**
- Create: `src/web/src/features/rider-app/hooks/useRiderPickupScan.ts`
- Create: `src/web/src/features/rider-app/hooks/useRecordFailedAttempt.ts`
- Create: `src/web/src/features/rider-app/hooks/useCompleteDelivery.ts`
- Test: `src/web/src/features/rider-app/hooks/useRiderPickupScan.test.tsx`
- Test: `src/web/src/features/rider-app/hooks/useCompleteDelivery.test.tsx`

**Interfaces:**
- Consumes: Task 4's `addOutboxItem`, Task 5's `drainOutbox`, Task 4's `RecordFailedAttemptFields`/`QueuedAction` types.
- Produces (used by Task 9 and Task 10): `useRiderPickupScan(riderId): UseMutationResult` — `mutate({ awb, shipmentId })`; `useRecordFailedAttempt(riderId): UseMutationResult` — `mutate(RecordFailedAttemptFields)`; `useCompleteDelivery(riderId): UseMutationResult` — `mutate({ shipmentId, recipientName, otpVerified, latitude, longitude, collectedCodAmount, signatureBlob, photoBlob })`. All three invalidate `["riders", "me", "tasks", "today"]` on enqueue (optimistic — the task list hides a task once its terminal action is queued, not once the server confirms it).

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/rider-app/hooks/useRiderPickupScan.test.tsx`:

```tsx
import "fake-indexeddb/auto";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { resetOutboxDbForTests, listOutboxItems } from "@/features/rider-app/sync/outboxDb";
import { useRiderPickupScan } from "./useRiderPickupScan";

vi.mock("@/features/rider-app/sync/syncManager", () => ({ drainOutbox: vi.fn() }));

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient();
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRiderPickupScan", () => {
  beforeEach(() => resetOutboxDbForTests());

  it("enqueues a pickup-scan action instead of calling fetch directly", async () => {
    const { result } = renderHook(() => useRiderPickupScan("r1"), { wrapper });

    result.current.mutate({ awb: "AWB001", shipmentId: "s1" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const items = await listOutboxItems();
    expect(items).toHaveLength(1);
    expect(items[0].action).toEqual({ kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" });
  });
});
```

Create `src/web/src/features/rider-app/hooks/useCompleteDelivery.test.tsx`:

```tsx
import "fake-indexeddb/auto";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { resetOutboxDbForTests, listOutboxItems } from "@/features/rider-app/sync/outboxDb";
import { useCompleteDelivery } from "./useCompleteDelivery";

vi.mock("@/features/rider-app/sync/syncManager", () => ({ drainOutbox: vi.fn() }));

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient();
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useCompleteDelivery", () => {
  beforeEach(() => resetOutboxDbForTests());

  it("enqueues a delivery-complete action carrying the raw blobs, not a pre-resolved storage key", async () => {
    const { result } = renderHook(() => useCompleteDelivery("r1"), { wrapper });
    const signatureBlob = new Blob(["sig"], { type: "image/png" });

    result.current.mutate({
      shipmentId: "s1",
      recipientName: "Recipient One",
      otpVerified: true,
      latitude: null,
      longitude: null,
      collectedCodAmount: null,
      signatureBlob,
      photoBlob: null,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const items = await listOutboxItems();
    expect(items).toHaveLength(1);
    expect(items[0].action.kind).toBe("delivery-complete");
    expect((items[0].action as { signatureBlob: Blob | null }).signatureBlob).toBe(signatureBlob);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- rider-app/hooks`
Expected: FAIL with "Cannot find module".

- [ ] **Step 3: Implement the three hooks**

Create `src/web/src/features/rider-app/hooks/useRiderPickupScan.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

export function useRiderPickupScan(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ awb, shipmentId }: { awb: string; shipmentId: string }) => {
      await addOutboxItem({ kind: "pickup-scan", riderId, shipmentId, awb });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

Create `src/web/src/features/rider-app/hooks/useRecordFailedAttempt.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";
import type { RecordFailedAttemptFields } from "@/features/rider-app/sync/types";

export function useRecordFailedAttempt(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: RecordFailedAttemptFields) => {
      await addOutboxItem({ kind: "delivery-fail", riderId, shipmentId: input.shipmentId, input });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

Create `src/web/src/features/rider-app/hooks/useCompleteDelivery.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

export interface CompleteDeliveryFields {
  shipmentId: string;
  recipientName: string;
  otpVerified: boolean;
  latitude: number | null;
  longitude: number | null;
  collectedCodAmount: number | null;
  signatureBlob: Blob | null;
  photoBlob: Blob | null;
}

export function useCompleteDelivery(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: CompleteDeliveryFields) => {
      await addOutboxItem({
        kind: "delivery-complete",
        riderId,
        shipmentId: input.shipmentId,
        recipientName: input.recipientName,
        otpVerified: input.otpVerified,
        latitude: input.latitude,
        longitude: input.longitude,
        collectedCodAmount: input.collectedCodAmount,
        signatureBlob: input.signatureBlob,
        photoBlob: input.photoBlob,
        signatureStorageKey: null,
        photoStorageKey: null,
      });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- rider-app/hooks`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/rider-app/hooks
git commit -m "feat(web): add outbox-aware rider mutation hooks"
```

---

### Task 7: Frontend — PWA manifest + Serwist service worker for `/rider`

**Files:**
- Create: `src/web/public/rider-manifest.webmanifest`
- Create: `src/web/public/icons/rider-icon.svg`
- Create: `src/web/src/app/rider/sw.ts`
- Modify: `src/web/next.config.mjs`

**Interfaces:**
- Produces: `public/sw-rider.js` (built by Serwist from `src/app/rider/sw.ts`), registered by Task 8's layout with `{ scope: "/rider/" }`; `public/rider-manifest.webmanifest`, linked by Task 8's layout.

- [ ] **Step 1: Create the manifest and icon**

Create `src/web/public/icons/rider-icon.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 192 192">
  <rect width="192" height="192" rx="32" fill="#0b0f14"/>
  <path d="M40 128h16l8-40h64l8 40h16M64 88h64" stroke="#f5f7fa" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
  <circle cx="68" cy="140" r="12" fill="#f5f7fa"/>
  <circle cx="124" cy="140" r="12" fill="#f5f7fa"/>
</svg>
```

(A simple delivery-van glyph as a functional placeholder — flagged for a real design pass, matching
the project's established practice of shipping a functional placeholder and scheduling a proper
visual pass separately, e.g. Frontend Phase F11's public tracking page.)

Create `src/web/public/rider-manifest.webmanifest`:

```json
{
  "name": "Courier Rider",
  "short_name": "Rider",
  "description": "Field app for pickup and delivery riders",
  "start_url": "/rider/tasks",
  "scope": "/rider/",
  "display": "standalone",
  "background_color": "#0b0f14",
  "theme_color": "#0b0f14",
  "icons": [
    { "src": "/icons/rider-icon.svg", "sizes": "any", "type": "image/svg+xml", "purpose": "any" }
  ]
}
```

- [ ] **Step 2: Create the service worker source**

Create `src/web/src/app/rider/sw.ts`:

```ts
import { defaultCache } from "@serwist/next/worker";
import { Serwist } from "serwist";
import type { PrecacheEntry, SerwistGlobalConfig } from "serwist";

declare global {
  interface WorkerGlobalScope extends SerwistGlobalConfig {
    __SW_MANIFEST: (PrecacheEntry | string)[] | undefined;
  }
}
declare const self: ServiceWorkerGlobalScope;

const serwist = new Serwist({
  precacheEntries: self.__SW_MANIFEST,
  skipWaiting: true,
  clientsClaim: true,
  navigationPreload: true,
  runtimeCaching: defaultCache,
});

serwist.addEventListeners();
```

- [ ] **Step 3: Wire Serwist into the Next.js build**

Replace `src/web/next.config.mjs`:

```js
import withSerwistInit from "@serwist/next";

const withSerwist = withSerwistInit({
  swSrc: "src/app/rider/sw.ts",
  swDest: "public/sw-rider.js",
  disable: process.env.NODE_ENV === "development",
});

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",
};

export default withSerwist(nextConfig);
```

- [ ] **Step 4: Verify the build produces the service worker**

Run: `npm run build`
Expected: build succeeds; `public/sw-rider.js` exists after the build (Serwist generates it at
build time — check with `ls public/sw-rider.js` after the build completes, non-empty file).

- [ ] **Step 5: Commit**

```bash
git add src/web/public/rider-manifest.webmanifest src/web/public/icons/rider-icon.svg src/web/src/app/rider/sw.ts src/web/next.config.mjs
git commit -m "feat(web): add PWA manifest and Serwist service worker scoped to /rider"
```

---

### Task 8: Frontend — `/rider` route guard + mobile shell layout

**Files:**
- Modify: `src/web/src/middleware.ts`
- Create: `src/web/src/components/layout/RiderShell.tsx`
- Create: `src/web/src/components/layout/OutboxStatusBar.tsx`
- Create: `src/web/src/app/rider/layout.tsx`
- Test: `src/web/src/components/layout/RiderShell.test.tsx`
- Test: `src/web/src/components/layout/OutboxStatusBar.test.tsx`

**Interfaces:**
- Consumes: Task 5's `useOutboxStatus`, Task 7's SW/manifest assets.
- Produces: `RiderShell` (bottom tab bar shell, wraps every `/rider/*` page), `OutboxStatusBar` (pending/stuck/last-synced indicator) — both consumed by `app/rider/layout.tsx`, which every later page task nests under.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/components/layout/OutboxStatusBar.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { OutboxStatusBar } from "./OutboxStatusBar";

vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: 2, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));

describe("OutboxStatusBar", () => {
  it("shows the pending queue count", () => {
    render(<OutboxStatusBar />);
    expect(screen.getByText(/2 pending/i)).toBeInTheDocument();
  });
});
```

Create `src/web/src/components/layout/RiderShell.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RiderShell } from "./RiderShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/rider/tasks" }));
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: 0, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));

describe("RiderShell", () => {
  it("renders the three bottom tabs and the page content", () => {
    render(<RiderShell><p>Page content</p></RiderShell>);
    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /tasks/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /scan/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /profile/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- RiderShell OutboxStatusBar`
Expected: FAIL with "Cannot find module".

- [ ] **Step 3: Implement `OutboxStatusBar` and `RiderShell`**

Create `src/web/src/components/layout/OutboxStatusBar.tsx`:

```tsx
"use client";

import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";

export function OutboxStatusBar() {
  const { pendingCount, stuckItems, lastSyncedAt } = useOutboxStatus();

  if (pendingCount === 0 && stuckItems.length === 0) {
    return null;
  }

  return (
    <div className="flex items-center justify-between border-b bg-muted px-4 py-1.5 text-xs text-muted-foreground">
      <span>
        {pendingCount > 0 && `${pendingCount} pending`}
        {pendingCount > 0 && stuckItems.length > 0 && " · "}
        {stuckItems.length > 0 && <span className="text-destructive">{stuckItems.length} needs review</span>}
      </span>
      {lastSyncedAt && <span>Last synced {new Date(lastSyncedAt).toLocaleTimeString()}</span>}
    </div>
  );
}
```

Create `src/web/src/components/layout/RiderShell.tsx`:

```tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ClipboardList, ScanLine, User } from "lucide-react";
import { cn } from "@/lib/utils";
import { OutboxStatusBar } from "./OutboxStatusBar";

const TABS = [
  { href: "/rider/tasks", label: "Tasks", icon: ClipboardList },
  { href: "/rider/scan", label: "Scan", icon: ScanLine },
  { href: "/rider/profile", label: "Profile", icon: User },
];

export function RiderShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <OutboxStatusBar />
      <main className="flex-1 overflow-y-auto pb-16">{children}</main>
      <nav className="fixed inset-x-0 bottom-0 flex border-t bg-background">
        {TABS.map((tab) => {
          const Icon = tab.icon;
          const active = pathname === tab.href;
          return (
            <Link
              key={tab.href}
              href={tab.href}
              className={cn(
                "flex flex-1 flex-col items-center gap-1 py-3 text-xs",
                active ? "text-primary" : "text-muted-foreground"
              )}
            >
              <Icon className="h-6 w-6" />
              {tab.label}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
```

- [ ] **Step 4: Add the route guard and root layout**

In `src/web/src/middleware.ts`, add `/rider/:path*` to the matcher (the existing session-cookie
check already applies to any matched path — no branch logic changes needed):

```ts
export const config = {
  matcher: ["/dashboard/:path*", "/shipments/:path*", "/branches/:path*", "/cod/:path*", "/merchant/:path*", "/rider/:path*"],
};
```

Create `src/web/src/app/rider/layout.tsx`. Next.js requires a `metadata` export (used here for the
manifest link) to live in a Server Component, so this file stays a plain (non-`"use client"`)
Server Component and delegates the service-worker/sync-manager side effects to a small client
child component:

```tsx
import { RiderShell } from "@/components/layout/RiderShell";
import { RiderServiceWorkerRegistration } from "@/components/layout/RiderServiceWorkerRegistration";

export const metadata = {
  manifest: "/rider-manifest.webmanifest",
};

export default function RiderLayout({ children }: { children: React.ReactNode }) {
  return (
    <RiderShell>
      <RiderServiceWorkerRegistration />
      {children}
    </RiderShell>
  );
}
```

Create `src/web/src/components/layout/RiderServiceWorkerRegistration.tsx`:

```tsx
"use client";

import { useEffect } from "react";
import { startSyncManager } from "@/features/rider-app/sync/syncManager";

export function RiderServiceWorkerRegistration() {
  useEffect(() => {
    if ("serviceWorker" in navigator) {
      navigator.serviceWorker.register("/sw-rider.js", { scope: "/rider/" }).catch(() => {});
    }
    return startSyncManager();
  }, []);

  return null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `npm test -- RiderShell OutboxStatusBar`
Expected: PASS.

- [ ] **Step 6: Verify the build still succeeds**

Run: `npm run build`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/middleware.ts src/web/src/components/layout/RiderShell.tsx src/web/src/components/layout/OutboxStatusBar.tsx src/web/src/components/layout/RiderServiceWorkerRegistration.tsx src/web/src/app/rider/layout.tsx src/web/src/components/layout/RiderShell.test.tsx src/web/src/components/layout/OutboxStatusBar.test.tsx
git commit -m "feat(web): add /rider route guard, mobile bottom-tab shell, and service worker registration"
```

---

### Task 9: Frontend — Today's Tasks page

**Files:**
- Create: `src/web/src/features/rider-app/store/riderAppPanelStore.ts`
- Create: `src/web/src/features/rider-app/components/RiderTaskCard.tsx`
- Create: `src/web/src/app/rider/tasks/page.tsx`
- Test: `src/web/src/features/rider-app/store/riderAppPanelStore.test.ts`
- Test: `src/web/src/features/rider-app/components/RiderTaskCard.test.tsx`

**Interfaces:**
- Consumes: existing `useMyRider`/`useMyTasksToday` (`@/features/riders/hooks/*`, read-only, reused as-is per the Global Constraints' scope note), Task 6's `useRiderPickupScan`.
- Produces: `useRiderAppPanelStore` — `{ panel: { mode: "closed" } | { mode: "record-attempt"; task: MyTask }; openRecordAttempt(task): void; close(): void }` (consumed by Task 10's sheet). `RiderTaskCard` (renders a "Record Attempt" button for `Delivery`-type tasks via a prop-supplied `onRecordAttempt` callback, so Task 10 doesn't need to modify this file).

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/rider-app/store/riderAppPanelStore.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { useRiderAppPanelStore } from "./riderAppPanelStore";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};

describe("riderAppPanelStore", () => {
  it("opens the record-attempt panel with the given task, then closes", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "record-attempt", task });

    useRiderAppPanelStore.getState().close();
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

Create `src/web/src/features/rider-app/components/RiderTaskCard.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { RiderTaskCard } from "./RiderTaskCard";
import type { MyTask } from "@/features/riders/types";

const pickupTask: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Pickup", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};
const deliveryTask: MyTask = { ...pickupTask, taskId: "t2", type: "Delivery", codAmount: 500 };

describe("RiderTaskCard", () => {
  it("shows recipient details and a COD badge when applicable", () => {
    render(<RiderTaskCard task={deliveryTask} riderId="r1" onRecordAttempt={vi.fn()} />);
    expect(screen.getByText("Recipient One")).toBeInTheDocument();
    expect(screen.getByText(/Rs\. 500/)).toBeInTheDocument();
  });

  it("calls onRecordAttempt for a Delivery task", () => {
    const onRecordAttempt = vi.fn();
    render(<RiderTaskCard task={deliveryTask} riderId="r1" onRecordAttempt={onRecordAttempt} />);
    fireEvent.click(screen.getByRole("button", { name: /record attempt/i }));
    expect(onRecordAttempt).toHaveBeenCalledWith(deliveryTask);
  });

  it("does not show a Record Attempt button for a Pickup task", () => {
    render(<RiderTaskCard task={pickupTask} riderId="r1" onRecordAttempt={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /record attempt/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- RiderTaskCard riderAppPanelStore`
Expected: FAIL with "Cannot find module".

- [ ] **Step 3: Implement the panel store, `RiderTaskCard`, and the tasks page**

Create `src/web/src/features/rider-app/store/riderAppPanelStore.ts`:

```ts
import { create } from "zustand";
import type { MyTask } from "@/features/riders/types";

type RiderAppPanelState =
  | { mode: "closed" }
  | { mode: "record-attempt"; task: MyTask };

interface RiderAppPanelStore {
  panel: RiderAppPanelState;
  openRecordAttempt: (task: MyTask) => void;
  close: () => void;
}

export const useRiderAppPanelStore = create<RiderAppPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRecordAttempt: (task) => set({ panel: { mode: "record-attempt", task } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Create `src/web/src/features/rider-app/components/RiderTaskCard.tsx`:

```tsx
"use client";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import type { MyTask } from "@/features/riders/types";

export function RiderTaskCard({
  task,
  riderId,
  onRecordAttempt,
}: {
  task: MyTask;
  riderId: string;
  onRecordAttempt: (task: MyTask) => void;
}) {
  const pickupScan = useRiderPickupScan(riderId);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>{task.type} &middot; {task.awb}</span>
          {task.codAmount > 0 && <Badge variant="outline">COD Rs. {task.codAmount}</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1 text-sm">
        <p className="font-medium text-foreground">{task.recipientName}</p>
        <p className="text-muted-foreground">{task.recipientPhone}</p>
        <p className="text-muted-foreground">{task.recipientAddressLine}, Ward {task.recipientWardNumber}</p>
        <div className="pt-2">
          {task.type === "Pickup" && (
            pickupScan.isSuccess ? (
              <p className="text-sm text-muted-foreground">Picked up.</p>
            ) : (
              <Button
                type="button"
                size="lg"
                className="w-full"
                disabled={pickupScan.isPending}
                onClick={() => pickupScan.mutate({ awb: task.awb, shipmentId: task.shipmentId })}
              >
                {pickupScan.isPending ? <Spinner className="h-4 w-4" /> : "Scan Pickup"}
              </Button>
            )
          )}
          {task.type === "Delivery" && (
            <Button type="button" size="lg" className="w-full" onClick={() => onRecordAttempt(task)}>
              Record Attempt
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
```

`riderId` comes from `useMyRider()` in the tasks page below and is passed straight through — not
derived from the task itself (`MyTask` carries no rider id; the rider viewing their own task list
*is* the rider, known once from the page's own `useMyRider()` call).

Create `src/web/src/app/rider/tasks/page.tsx`:

```tsx
"use client";

import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { Spinner } from "@/components/ui/spinner";
import { RiderTaskCard } from "@/features/rider-app/components/RiderTaskCard";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";

export default function RiderTasksPage() {
  const { data: rider, isLoading: riderLoading, isError: riderError } = useMyRider();
  const { data: tasksToday, isLoading: tasksLoading, isError: tasksError, refetch } = useMyTasksToday(!!rider);

  if (riderLoading || (rider && tasksLoading)) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (riderError) {
    return <EmptyState title="No rider profile" description="This account isn't linked to a rider record." />;
  }

  if (tasksError) {
    return <ErrorState title="Couldn't load today's deliveries" description="Something went wrong." onRetry={() => refetch()} />;
  }

  const tasks = tasksToday?.tasks ?? [];

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Today&apos;s Tasks</h1>
      {tasks.length === 0 ? (
        <EmptyState title="No deliveries assigned today" description="Pickup and delivery tasks assigned to you today will show up here." />
      ) : (
        <div className="space-y-3">
          {tasks.map((task) => (
            <RiderTaskCard
              key={task.taskId}
              task={task}
              riderId={rider!.id}
              onRecordAttempt={(t) => useRiderAppPanelStore.getState().openRecordAttempt(t)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- RiderTaskCard riderAppPanelStore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/rider-app/store/riderAppPanelStore.ts src/web/src/features/rider-app/store/riderAppPanelStore.test.ts src/web/src/features/rider-app/components/RiderTaskCard.tsx src/web/src/features/rider-app/components/RiderTaskCard.test.tsx src/web/src/app/rider/tasks/page.tsx
git commit -m "feat(web): add rider app Today's Tasks page"
```

---

### Task 10: Frontend — Record Attempt bottom sheet

**Files:**
- Create: `src/web/src/features/rider-app/components/RiderRecordAttemptSheet.tsx`
- Create: `src/web/src/features/rider-app/components/RiderSheets.tsx`
- Modify: `src/web/src/app/rider/layout.tsx` (mount the sheet, same pattern as `(staff)/layout.tsx`)
- Test: `src/web/src/features/rider-app/components/RiderRecordAttemptSheet.test.tsx`

**Interfaces:**
- Consumes: Task 9's `useRiderAppPanelStore`, Task 6's `useRecordFailedAttempt`/`useCompleteDelivery`, existing `SignaturePad` (`@/features/riders/components/SignaturePad`, reused as-is — it's already a generic canvas component with no offline-specific behavior).

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/rider-app/components/RiderRecordAttemptSheet.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { RiderRecordAttemptSheet } from "./RiderRecordAttemptSheet";
import type { MyTask } from "@/features/riders/types";

const failMutate = vi.fn();
const completeMutate = vi.fn();
vi.mock("@/features/rider-app/hooks/useRecordFailedAttempt", () => ({
  useRecordFailedAttempt: () => ({ mutateAsync: failMutate, isPending: false }),
}));
vi.mock("@/features/rider-app/hooks/useCompleteDelivery", () => ({
  useCompleteDelivery: () => ({ mutateAsync: completeMutate, isPending: false }),
}));

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};

function renderSheet() {
  const queryClient = new QueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderRecordAttemptSheet riderId="r1" />
    </QueryClientProvider>
  );
}

describe("RiderRecordAttemptSheet", () => {
  beforeEach(() => {
    failMutate.mockReset().mockResolvedValue({});
    completeMutate.mockReset().mockResolvedValue({});
    useRiderAppPanelStore.getState().close();
  });

  it("submits a failure attempt with the selected reason", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    renderSheet();

    fireEvent.click(screen.getByRole("button", { name: "Failed" }));
    fireEvent.click(screen.getByLabelText("Reason"));
    fireEvent.click(await screen.findByText("CustomerUnavailable"));
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(failMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", reason: "CustomerUnavailable" })
      )
    );
  });

  it("submits a successful delivery with recipient name and blobs", async () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    renderSheet();

    fireEvent.change(screen.getByLabelText("Recipient Name"), { target: { value: "Recipient One" } });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(completeMutate).toHaveBeenCalledWith(
        expect.objectContaining({ shipmentId: "s1", recipientName: "Recipient One" })
      )
    );
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- rider-app/components/RiderRecordAttemptSheet`
Expected: FAIL with "Cannot find module './RiderRecordAttemptSheet'".

- [ ] **Step 3: Implement the sheet**

Create `src/web/src/features/rider-app/components/RiderRecordAttemptSheet.tsx` (a bottom-sheet
adaptation of the F5 `RecordAttemptSheet`, wired to the outbox-aware hooks):

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { SignaturePad } from "@/features/riders/components/SignaturePad";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { useRecordFailedAttempt } from "@/features/rider-app/hooks/useRecordFailedAttempt";
import { useCompleteDelivery } from "@/features/rider-app/hooks/useCompleteDelivery";
import type { RecordFailedAttemptFields } from "@/features/rider-app/sync/types";

const FAILURE_REASONS: RecordFailedAttemptFields["reason"][] = [
  "CustomerUnavailable", "PhoneUnreachable", "WrongAddress", "IncompleteAddress",
  "CustomerRefused", "CustomerRequestedReschedule", "CashUnavailable", "DamagedParcel", "Other",
];

export function RiderRecordAttemptSheet({ riderId }: { riderId: string }) {
  const panel = useRiderAppPanelStore((s) => s.panel);
  const close = useRiderAppPanelStore((s) => s.close);
  const isOpen = panel.mode === "record-attempt";
  const task = panel.mode === "record-attempt" ? panel.task : null;

  const [outcome, setOutcome] = useState<"success" | "failure">("success");
  const [reason, setReason] = useState<RecordFailedAttemptFields["reason"] | undefined>(undefined);
  const [notes, setNotes] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [otpVerified, setOtpVerified] = useState(false);
  const [collectedCodAmount, setCollectedCodAmount] = useState("");
  const [signatureBlob, setSignatureBlob] = useState<Blob | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [latitude, setLatitude] = useState<number | null>(null);
  const [longitude, setLongitude] = useState<number | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  const recordFailedAttempt = useRecordFailedAttempt(riderId);
  const completeDelivery = useCompleteDelivery(riderId);

  function reset() {
    setOutcome("success");
    setReason(undefined);
    setNotes("");
    setRecipientName("");
    setOtpVerified(false);
    setCollectedCodAmount("");
    setSignatureBlob(null);
    setPhotoFile(null);
    setLatitude(null);
    setLongitude(null);
    setServerError(null);
  }

  function captureLocation() {
    if (!("geolocation" in navigator)) return;
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLatitude(position.coords.latitude);
        setLongitude(position.coords.longitude);
      },
      () => {}
    );
  }

  async function handleSubmitFailure() {
    if (!task || !reason) return;
    setServerError(null);
    try {
      await recordFailedAttempt.mutateAsync({
        shipmentId: task.shipmentId,
        reason,
        notes: notes || undefined,
        customerContacted: true,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't queue this attempt. Try again.");
    }
  }

  const codRequired = task ? task.codAmount > 0 : false;
  const codInvalid = codRequired && (collectedCodAmount === "" || Number(collectedCodAmount) < 0);
  const canSubmitSuccess = recipientName.trim().length > 0 && !codInvalid;

  async function handleSubmitSuccess() {
    if (!task || !canSubmitSuccess) return;
    setServerError(null);
    try {
      await completeDelivery.mutateAsync({
        shipmentId: task.shipmentId,
        recipientName: recipientName.trim(),
        otpVerified,
        latitude,
        longitude,
        collectedCodAmount: codRequired ? Number(collectedCodAmount) : null,
        signatureBlob,
        photoBlob: photoFile,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't queue this delivery. Try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent side="bottom" className="max-h-[90vh] overflow-y-auto rounded-t-2xl">
        <SheetHeader>
          <SheetTitle>Record Delivery Attempt</SheetTitle>
        </SheetHeader>
        {task && (
          <div className="mt-6 space-y-4">
            <div className="flex gap-2">
              <Button type="button" variant={outcome === "success" ? "default" : "outline"} size="lg" className="flex-1" onClick={() => setOutcome("success")}>
                Delivered
              </Button>
              <Button type="button" variant={outcome === "failure" ? "default" : "outline"} size="lg" className="flex-1" onClick={() => setOutcome("failure")}>
                Failed
              </Button>
            </div>

            {outcome === "failure" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="attempt-reason">Reason</Label>
                  <Select value={reason} onValueChange={(v) => setReason(v as RecordFailedAttemptFields["reason"])}>
                    <SelectTrigger id="attempt-reason" aria-label="Reason">
                      <SelectValue placeholder="Select reason" />
                    </SelectTrigger>
                    <SelectContent>
                      {FAILURE_REASONS.map((r) => <SelectItem key={r} value={r}>{r}</SelectItem>)}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="attempt-notes">Notes</Label>
                  <Textarea id="attempt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
                {serverError && <FieldError id="record-attempt-server-error">{serverError}</FieldError>}
                <Button type="button" size="lg" className="w-full" disabled={!reason || recordFailedAttempt.isPending} onClick={handleSubmitFailure}>
                  {recordFailedAttempt.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
              </div>
            )}

            {outcome === "success" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="recipient-name">Recipient Name</Label>
                  <Input id="recipient-name" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} />
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox id="otp-verified" checked={otpVerified} onCheckedChange={(v) => setOtpVerified(v === true)} />
                  <Label htmlFor="otp-verified">Customer verified via OTP</Label>
                </div>
                <div className="space-y-2">
                  <Label>Signature</Label>
                  <SignaturePad onChange={setSignatureBlob} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="pod-photo">Photo</Label>
                  <input id="pod-photo" type="file" accept="image/*" capture="environment" onChange={(e) => setPhotoFile(e.target.files?.[0] ?? null)} />
                </div>
                <Button type="button" variant="outline" size="sm" onClick={captureLocation}>
                  {latitude !== null ? "Location captured" : "Capture current location"}
                </Button>
                {task.codAmount > 0 && (
                  <div className="space-y-2">
                    <Label htmlFor="collected-cod">Collected COD Amount</Label>
                    <Input id="collected-cod" type="number" value={collectedCodAmount} onChange={(e) => setCollectedCodAmount(e.target.value)} placeholder={String(task.codAmount)} />
                  </div>
                )}
                {serverError && <FieldError id="record-attempt-server-error">{serverError}</FieldError>}
                <Button type="button" size="lg" className="w-full" disabled={!canSubmitSuccess || completeDelivery.isPending} onClick={handleSubmitSuccess}>
                  {completeDelivery.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

Mount it in `src/web/src/app/rider/layout.tsx` (add alongside `RiderServiceWorkerRegistration`,
inside `RiderShell` — needs `riderId`, so lift a small client component that reads `useMyRider()`
and only renders the sheet once a rider id is known):

```tsx
"use client";

import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { RiderRecordAttemptSheet } from "@/features/rider-app/components/RiderRecordAttemptSheet";

export function RiderSheets() {
  const { data: rider } = useMyRider();
  if (!rider) return null;
  return <RiderRecordAttemptSheet riderId={rider.id} />;
}
```

Save this as `src/web/src/features/rider-app/components/RiderSheets.tsx`, then update
`src/web/src/app/rider/layout.tsx`:

```tsx
import { RiderShell } from "@/components/layout/RiderShell";
import { RiderServiceWorkerRegistration } from "@/components/layout/RiderServiceWorkerRegistration";
import { RiderSheets } from "@/features/rider-app/components/RiderSheets";

export const metadata = {
  manifest: "/rider-manifest.webmanifest",
};

export default function RiderLayout({ children }: { children: React.ReactNode }) {
  return (
    <RiderShell>
      <RiderServiceWorkerRegistration />
      {children}
      <RiderSheets />
    </RiderShell>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm test -- rider-app/components/RiderRecordAttemptSheet`
Expected: PASS.

- [ ] **Step 5: Verify the build**

Run: `npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/rider-app/components/RiderRecordAttemptSheet.tsx src/web/src/features/rider-app/components/RiderRecordAttemptSheet.test.tsx src/web/src/features/rider-app/components/RiderSheets.tsx src/web/src/app/rider/layout.tsx
git commit -m "feat(web): add rider app record-attempt bottom sheet"
```

---

### Task 11: Frontend — Camera barcode scanner + Scan page

**Files:**
- Create: `src/web/src/features/rider-app/components/BarcodeScanner.tsx`
- Create: `src/web/src/app/rider/scan/page.tsx`
- Test: `src/web/src/features/rider-app/components/BarcodeScanner.test.tsx`

**Interfaces:**
- Consumes: Task 6's `useRiderPickupScan`, existing `useMyRider`/`useMyTasksToday`, Task 10's `useRiderAppPanelStore`.
- Produces: `BarcodeScanner({ onDetect: (awb: string) => void })` — reusable if a later phase needs scanning elsewhere.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/rider-app/components/BarcodeScanner.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { BarcodeScanner } from "./BarcodeScanner";

describe("BarcodeScanner", () => {
  beforeEach(() => {
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] }) },
    });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("calls onDetect with the trimmed, uppercased value from manual entry, without ever requesting the camera", () => {
    const onDetect = vi.fn();
    render(<BarcodeScanner onDetect={onDetect} />);

    fireEvent.change(screen.getByLabelText(/enter awb manually/i), { target: { value: " awb001 " } });
    fireEvent.click(screen.getByRole("button", { name: /use this awb/i }));

    expect(onDetect).toHaveBeenCalledWith("AWB001");
    expect(navigator.mediaDevices.getUserMedia).not.toHaveBeenCalled();
  });

  it("does not request the camera on mount — only after the user taps Start Scanning", async () => {
    const detect = vi.fn().mockResolvedValue([{ rawValue: "AWB002" }]);
    vi.stubGlobal("BarcodeDetector", vi.fn().mockImplementation(() => ({ detect })));
    const onDetect = vi.fn();

    render(<BarcodeScanner onDetect={onDetect} />);
    expect(navigator.mediaDevices.getUserMedia).not.toHaveBeenCalled();
    expect(window.BarcodeDetector).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /start scanning/i }));

    // The component's detection loop is driven by requestAnimationFrame in real use; this only
    // verifies BarcodeDetector was constructed once scanning starts, not the full camera loop
    // (covered by the manual E2E pass in Task 14 — jsdom has no real camera/video pipeline).
    await waitFor(() => expect(window.BarcodeDetector).toHaveBeenCalled());
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- BarcodeScanner`
Expected: FAIL with "Cannot find module './BarcodeScanner'".

- [ ] **Step 3: Implement the scanner**

Create `src/web/src/features/rider-app/components/BarcodeScanner.tsx`:

```tsx
"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

declare global {
  interface Window {
    BarcodeDetector?: new (options?: { formats?: string[] }) => {
      detect: (source: CanvasImageSource) => Promise<{ rawValue: string }[]>;
    };
  }
}

export function BarcodeScanner({ onDetect }: { onDetect: (awb: string) => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [manualValue, setManualValue] = useState("");
  const [cameraError, setCameraError] = useState<string | null>(null);
  // Camera access is requested only after an explicit tap, never on mount — CLAUDE.md §33 and the
  // design spec's §7 security note both call this out: a rider opening /rider/scan shouldn't be
  // hit with a permission prompt before choosing to use the camera at all (manual entry is always
  // available with no prompt).
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    if (!scanning) return;
    let stream: MediaStream | null = null;
    let cancelled = false;
    let rafId: number | null = null;

    async function start() {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
        if (cancelled || !videoRef.current) return;
        videoRef.current.srcObject = stream;
        await videoRef.current.play();

        if (window.BarcodeDetector) {
          const detector = new window.BarcodeDetector();
          const loop = async () => {
            if (cancelled || !videoRef.current) return;
            try {
              const results = await detector.detect(videoRef.current);
              if (results[0]?.rawValue) {
                onDetect(results[0].rawValue.trim().toUpperCase());
                return;
              }
            } catch {
              // transient decode failure — keep looping
            }
            rafId = requestAnimationFrame(loop);
          };
          rafId = requestAnimationFrame(loop);
        } else {
          const { BrowserMultiFormatReader } = await import("@zxing/browser");
          const reader = new BrowserMultiFormatReader();
          const controls = await reader.decodeFromVideoElement(videoRef.current, (result) => {
            if (result && !cancelled) {
              onDetect(result.getText().trim().toUpperCase());
              controls.stop();
            }
          });
        }
      } catch {
        setCameraError("Camera unavailable — use manual entry below.");
      }
    }

    void start();
    return () => {
      cancelled = true;
      if (rafId) cancelAnimationFrame(rafId);
      stream?.getTracks().forEach((track) => track.stop());
    };
  }, [scanning, onDetect]);

  function submitManual() {
    const trimmed = manualValue.trim().toUpperCase();
    if (trimmed) onDetect(trimmed);
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="relative aspect-square w-full overflow-hidden rounded-lg bg-black">
        {scanning ? (
          <video ref={videoRef} className="h-full w-full object-cover" muted playsInline />
        ) : (
          <div className="flex h-full w-full items-center justify-center">
            <Button type="button" size="lg" onClick={() => setScanning(true)}>Start Scanning</Button>
          </div>
        )}
        {cameraError && <p className="absolute inset-x-0 bottom-2 text-center text-sm text-white">{cameraError}</p>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="manual-awb">Enter AWB manually</Label>
        <div className="flex gap-2">
          <Input id="manual-awb" value={manualValue} onChange={(e) => setManualValue(e.target.value)} />
          <Button type="button" onClick={submitManual}>Use this AWB</Button>
        </div>
      </div>
    </div>
  );
}
```

Create `src/web/src/app/rider/scan/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { BarcodeScanner } from "@/features/rider-app/components/BarcodeScanner";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";

export default function RiderScanPage() {
  const { data: rider } = useMyRider();
  const { data: tasksToday } = useMyTasksToday(!!rider);
  const pickupScan = useRiderPickupScan(rider?.id ?? "");
  const [message, setMessage] = useState<string | null>(null);

  function handleDetect(awb: string) {
    const task = tasksToday?.tasks.find((t) => t.awb === awb);
    if (!task) {
      setMessage(`No task found for AWB ${awb}.`);
      return;
    }
    setMessage(null);
    if (task.type === "Pickup") {
      pickupScan.mutate({ awb: task.awb, shipmentId: task.shipmentId });
    } else {
      useRiderAppPanelStore.getState().openRecordAttempt(task);
    }
  }

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Scan</h1>
      {message && <p className="text-sm text-destructive">{message}</p>}
      <BarcodeScanner onDetect={handleDetect} />
    </div>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- BarcodeScanner`
Expected: PASS.

- [ ] **Step 5: Verify the build**

Run: `npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/rider-app/components/BarcodeScanner.tsx src/web/src/features/rider-app/components/BarcodeScanner.test.tsx src/web/src/app/rider/scan/page.tsx
git commit -m "feat(web): add camera barcode scanner and rider app Scan page"
```

---

### Task 12: Frontend — Profile page

**Files:**
- Create: `src/web/src/app/rider/profile/page.tsx`
- Test: `src/web/src/app/rider/profile/page.test.tsx`

**Interfaces:**
- Consumes: `useMyRider`, Task 5's `useOutboxStatus`, existing `useSession`/`POST /api/auth/logout`.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/app/rider/profile/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import RiderProfilePage from "./page";

vi.mock("@/features/riders/hooks/useMyRider", () => ({
  useMyRider: () => ({ data: { id: "r1", licenseNumber: "LIC-001", roleType: "DeliveryRider" }, isLoading: false }),
}));
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({
    pendingCount: 1,
    stuckItems: [{ id: "i1", action: { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" }, idempotencyKey: "k1", createdAt: 0, status: "stuck", lastError: "wrong state" }],
    lastSyncedAt: null,
    refresh: vi.fn(),
  }),
}));

describe("RiderProfilePage", () => {
  it("shows the rider's license number and any stuck queue items needing review", () => {
    render(<RiderProfilePage />);
    expect(screen.getByText("LIC-001")).toBeInTheDocument();
    expect(screen.getByText(/wrong state/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- rider/profile`
Expected: FAIL with "Cannot find module './page'".

- [ ] **Step 3: Implement the profile page**

Create `src/web/src/app/rider/profile/page.tsx`:

```tsx
"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";

export default function RiderProfilePage() {
  const { data: rider, isLoading } = useMyRider();
  const { stuckItems } = useOutboxStatus();
  const router = useRouter();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Profile</h1>
      {rider && (
        <Card>
          <CardHeader><CardTitle>{rider.roleType}</CardTitle></CardHeader>
          <CardContent className="space-y-1 text-sm">
            <p className="text-muted-foreground">License: {rider.licenseNumber}</p>
          </CardContent>
        </Card>
      )}
      {stuckItems.length > 0 && (
        <Card>
          <CardHeader><CardTitle className="text-destructive">Needs Review</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            {stuckItems.map((item) => (
              <div key={item.id} className="rounded-md border border-destructive/40 p-3 text-sm">
                <p className="font-medium">{item.action.kind} &middot; shipment {item.action.shipmentId}</p>
                <p className="text-muted-foreground">{item.lastError}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
      <Button type="button" variant="outline" size="lg" className="w-full" onClick={handleLogout}>
        Log Out
      </Button>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm test -- rider/profile`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/rider/profile
git commit -m "feat(web): add rider app Profile page with sync-status review"
```

---

### Task 13: Frontend — Retire the desktop `my-deliveries` surface

**Files:**
- Delete: `src/web/src/app/(staff)/my-deliveries/page.tsx`
- Delete: `src/web/src/features/riders/components/RecordAttemptSheet.tsx`
- Delete: `src/web/src/features/riders/components/RecordAttemptSheet.test.tsx`
- Delete: `src/web/src/features/riders/components/PickupScanControl.tsx`
- Delete: `src/web/src/features/riders/components/PickupScanControl.test.tsx`
- Delete: `src/web/src/features/riders/store/myDeliveriesPanelStore.ts`
- Delete: `src/web/src/features/riders/store/myDeliveriesPanelStore.test.ts`
- Delete: `src/web/src/features/riders/hooks/useRiderPickupScan.ts`
- Delete: `src/web/src/features/riders/hooks/useRecordFailedAttempt.ts`
- Delete: `src/web/src/features/riders/hooks/useCompleteDelivery.ts`
- Delete: `src/web/src/features/riders/hooks/usePodUpload.ts`
- Modify: `src/web/src/components/nav-main.tsx`

**Interfaces:** none — this task only removes now-superseded code and repoints one nav link.

- [ ] **Step 1: Confirm nothing outside the deleted set still imports the deleted files**

Run: `grep -rl "my-deliveries\|RecordAttemptSheet\|PickupScanControl\|myDeliveriesPanelStore\|features/riders/hooks/useRiderPickupScan\|features/riders/hooks/useRecordFailedAttempt\|features/riders/hooks/useCompleteDelivery\|features/riders/hooks/usePodUpload" src/web/src --include=*.tsx --include=*.ts`
Expected output: only the files listed above, plus `src/web/src/components/nav-main.tsx` (handled
in Step 3) and `src/web/src/app/(staff)/layout.tsx` if it references `RecordAttemptSheet` (it
doesn't today — only `RiderCreateSheet`/`VehicleCreateSheet`/`AssignTaskSheet` are dispatcher-side
and stay). If anything else appears, stop and investigate before deleting — this plan's earlier
tasks were built with zero shared imports into `features/riders/*` specifically so this deletion
would be clean; an unexpected hit means something drifted.

- [ ] **Step 2: Delete the files**

```bash
git rm src/web/src/app/\(staff\)/my-deliveries/page.tsx
git rm src/web/src/features/riders/components/RecordAttemptSheet.tsx src/web/src/features/riders/components/RecordAttemptSheet.test.tsx
git rm src/web/src/features/riders/components/PickupScanControl.tsx src/web/src/features/riders/components/PickupScanControl.test.tsx
git rm src/web/src/features/riders/store/myDeliveriesPanelStore.ts src/web/src/features/riders/store/myDeliveriesPanelStore.test.ts
git rm src/web/src/features/riders/hooks/useRiderPickupScan.ts src/web/src/features/riders/hooks/useRecordFailedAttempt.ts src/web/src/features/riders/hooks/useCompleteDelivery.ts src/web/src/features/riders/hooks/usePodUpload.ts
```

- [ ] **Step 3: Repoint the nav entry**

In `src/web/src/components/nav-main.tsx`, change the `"My Deliveries"` entry's `href` from
`/my-deliveries` to `/rider/tasks` (keep the same permission gate and icon — the label still makes
sense as a staff-sidebar entry point into the rider PWA, e.g. for a rider using a shared office PC
before ever installing to a phone home screen):

```ts
  { href: "/rider/tasks", label: "My Deliveries", permission: "delivery.attempt", icon: ClipboardCheck },
```

- [ ] **Step 4: Run the full frontend test suite and build**

Run: `npm test && npm run build`
Expected: PASS, no leftover references to the deleted files, no broken imports.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/components/nav-main.tsx
git commit -m "refactor(web): retire desktop my-deliveries surface, superseded by /rider PWA"
```

---

### Task 14: Manual E2E verification + docs update

This task has no automated-test steps — it's the same live-verification convention every prior
Frontend Roadmap phase has closed with (F5 through F11), applied here, plus the offline-specific
checks this phase specifically needs (no automated test proves real offline behavior end-to-end,
per the design spec §6).

- [ ] **Step 1: Start both servers against real Postgres**

Run the backend (`dotnet run` from `src/Api`) and frontend (`npm run dev` from `src/web`) against
the shared local Postgres container.

- [ ] **Step 2: Seed a real scenario via the API**

Register a fresh org, seed two branches with delivery zones, a rate card, a merchant, a pickup
rider, and a delivery rider — the same request shapes `RidersEndpointsTests.cs` already uses.

- [ ] **Step 3: Walk the online path in the browser**

Log in as the delivery rider, open `/rider/tasks`, confirm the bottom tab bar renders, scan a
pickup (manual entry), move a shipment to `AtDestinationBranch` via the existing staff Manifests
UI, assign a delivery task, and complete a delivery through `/rider/tasks` with a drawn signature,
a photo, and GPS — confirm the shipment reaches `Delivered` and the queue shows zero pending items
throughout (never actually queued, since the network was live).

- [ ] **Step 4: Walk the offline path**

Using Chrome DevTools' Network throttling set to "Offline," record a second delivery attempt (or a
pickup scan) — confirm the action completes optimistically in the UI, the `OutboxStatusBar` shows
a pending count, and the item persists across a page reload (real IndexedDB, not memory). Re-enable
the network, confirm the outbox drains automatically within 30 seconds (or immediately on the next
`online` event / tab focus) and the shipment's real state on the server matches what was recorded
offline.

- [ ] **Step 5: Walk the camera-scanning path**

On a real mobile device (or a desktop Chrome with a webcam), open `/rider/scan`, grant camera
permission, and scan a printed AWB barcode/QR for a real seeded task — confirm it routes to the
correct action (pickup-scan or record-attempt) per Task 11's lookup logic. Deny camera permission
once and confirm the manual-entry fallback still works end to end.

- [ ] **Step 6: Run a Lighthouse PWA audit**

On a production build (`npm run build && npm run start`), run Chrome DevTools' Lighthouse PWA
category against `/rider/tasks`. Confirm: installable, manifest valid, service worker registered,
viewport configured. Fix anything it flags before closing this phase — record the fix as a new
step here if one is needed (do not silently skip a failing audit item).

- [ ] **Step 7: Update `docs/FRONTEND-ROADMAP.md`**

Replace the "Later" section's F12 bullet with a full phase entry (matching every other phase's
format: entry/exit criteria, then a `*Status: Complete (YYYY-MM-DD).*` paragraph summarizing what
shipped, the backend gap closed, and any real bugs found during Steps 3-6 — following this
project's standing convention of recording exactly what was found and fixed, not just what was
planned).

- [ ] **Step 8: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F12 (Rider Mobile-Web PWA) complete"
```
