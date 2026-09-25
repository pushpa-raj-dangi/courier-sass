using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierBookingEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierBookingEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private static async Task<Guid> CreateManualAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid shipmentId, Guid accountId, string carrierAwb, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb })
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Book_Replay_Cancel_Rebook_ManualCarrier()
    {
        var org = await SetUpOrgAsync("book-flow");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var key = Guid.NewGuid().ToString("N");

        var first = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var booked = (await first.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;
        Assert.Equal("Booked", booked.Status);
        Assert.Equal("MAN-100", booked.CarrierAwb);

        var replay = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(booked.Id, (await replay.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Id);

        var cancel = await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/booking/cancel", null);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Status);

        var rebook = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-101", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, rebook.StatusCode);
    }

    [Fact]
    public async Task Book_WithoutIdempotencyKey_Returns400()
    {
        var org = await SetUpOrgAsync("book-nokey");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Book_BeforeCustomsPrepared_Returns400()
    {
        var org = await SetUpOrgAsync("book-early");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await CreateOutboundAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LinkInbound_OnCreatedInboundShipment_Succeeds_AndOnOutbound_Returns400()
    {
        var org = await SetUpOrgAsync("link");
        var accountId = await CreateManualAccountAsync(org.Client);
        var inbound = await CreateInboundAsync(org);
        var outbound = await CreateOutboundAsync(org);

        var link = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-1", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal("Inbound", (await link.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Direction);

        var wrong = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{outbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-2", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    [Fact]
    public async Task OtherTenantsShipment_Returns404_AndMissingPermission_Returns403()
    {
        var owner = await SetUpOrgAsync("book-owner");
        var other = await SetUpOrgAsync("book-other");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(owner);
        var otherAccount = await CreateManualAccountAsync(other.Client);

        var crossTenant = await BookAsync(other.Client, shipment.Shipment.Id, otherAccount, "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var readOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.read" }, null);
        var forbidden = await BookAsync(readOnly, shipment.Shipment.Id, Guid.NewGuid(), "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task UserScopedToAnotherBranch_Returns404()
    {
        var org = await SetUpOrgAsync("book-scope");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var otherBranch = await CreateBranchAsync(org.Client, $"OB-{Guid.NewGuid():N}"[..10]);
        var scoped = await SeedUserClientAsync(org.Auth, new[] { "international.book" }, otherBranch);

        var response = await BookAsync(scoped, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Fix round 3, finding 1: books through CarrierBookingService directly (in its own tenant
    // scope, keeping that scope's DbContext alive afterward), against real Postgres, so the
    // relational ExecuteUpdate/accept-into-tracker path is actually exercised (the InMemory unit
    // tests never take that branch). A concurrent Cancel then lands through a completely separate
    // scope/connection; a bare SaveChangesAsync on the original booking scope afterward must not
    // disturb it.
    //
    // Caveat (see the fix round 3 report for the fuller explanation): in this codebase, AuditLogger
    // .LogAsync always calls SaveChangesAsync on the same shared DbContext at the end of every
    // audited operation, including the one CompleteBookingAsync performs right before returning on
    // its success path. That flush closes the "dirty tracked entity" window before BookAsync ever
    // returns to this test — so, on its own, this particular book-then-cancel-then-save sequence
    // cannot distinguish the pre-fix ExecuteUpdate-without-accepting bug from the fix (it passes
    // either way, and does here — verified by temporarily checking out e2cda3c's version of
    // CarrierBookingService.cs and confirming the same). The actual bypass-the-tracker mechanism is
    // instead proven at the unit level (BookAsync_Success_LeavesTheTrackedEntityUnchanged and
    // BookAsync_LateFailure_DoesNotOverwriteARowAlreadyBookedByAConcurrentAttempt in
    // CarrierBookingServiceTests.cs), where a provider hook can inject the race deterministically.
    // This test is kept as a real, coarse end-to-end safety net for the same user-visible invariant
    // ("a Cancel, once persisted, is never later reverted by ordinary booking activity") against the
    // real database and the real DI-wired CarrierBookingService.
    [Fact]
    public async Task Book_ThenAConcurrentCancelFromAnotherScope_SurvivesASubsequentSaveOnTheBookingScope()
    {
        var org = await SetUpOrgAsync("book-race-cancel");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var tenantScopes = Factory.Services.GetRequiredService<ITenantScopeFactory>();
        await using var bookingScope = tenantScopes.CreateScope(org.Auth.OrganizationId);
        var service = bookingScope.ServiceProvider.GetRequiredService<CarrierBookingService>();
        var bookingDbContext = bookingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actingUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", org.Auth.UserId.ToString()) }, "test"));

        var booked = await service.BookAsync(shipment.Shipment.Id, new BookCarrierRequest(accountId, null, "MAN-RACE"),
            Guid.NewGuid().ToString("N"), actingUser, CancellationToken.None);
        Assert.Equal("Booked", booked!.Status);

        using (var cancelScope = Factory.Services.CreateScope())
        {
            var cancelDbContext = cancelScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await cancelDbContext.Set<CarrierBooking>().IgnoreQueryFilters().SingleAsync(b => b.Id == booked.Id);
            row.Status = CarrierBookingStatus.Cancelled;
            row.CancelledAt = DateTimeOffset.UtcNow;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await cancelDbContext.SaveChangesAsync();
        }

        // By this point AuditLogger.LogAsync has already flushed the booking scope's context once
        // (see the caveat above), so the tracker has nothing left to save here either way — this
        // call documents and locks in that expectation rather than being the sole proof of the fix.
        await bookingDbContext.SaveChangesAsync();

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var final = await verifyDbContext.Set<CarrierBooking>().IgnoreQueryFilters().SingleAsync(b => b.Id == booked.Id);
        Assert.Equal(CarrierBookingStatus.Cancelled, final.Status);
    }
}
