using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Bare System.Net.HttpListener stub, not a new test-only package — mirrors this project's
// convention of a hand-written fake over an external dependency (see Webhooks.Tests'
// FakeHttpMessageHandler), just as a real listening HTTP server this time, since
// WebhooksConsumer's real HttpClient makes a genuine network call over the real loopback
// interface, not something an in-process fake can intercept.
file class StubWebhookTarget : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public int ResponseStatusCode { get; set; } = 200;
    public int RequestCount { get; private set; }
    public string? LastSignatureHeader { get; private set; }
    public string? LastBody { get; private set; }
    public string Url { get; }

    public StubWebhookTarget()
    {
        var port = GetFreePort();
        Url = $"http://127.0.0.1:{port}/hook";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
            {
                return;
            }
            RequestCount++;
            LastSignatureHeader = context.Request.Headers["X-Webhook-Signature"];
            using var reader = new StreamReader(context.Request.InputStream);
            LastBody = await reader.ReadToEndAsync();
            context.Response.StatusCode = ResponseStatusCode;
            context.Response.Close();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}

public class NotificationsAndWebhooksEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public NotificationsAndWebhooksEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    // Returns owner login credentials alongside ContactEmail — the first test only needs
    // ContactEmail (to poll the mail-catcher), the second needs OwnerEmail/Password (to log in
    // as this merchant and manage its own webhook subscription).
    private async Task<(Guid MerchantId, string ContactEmail, string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient)
    {
        var contactEmail = $"contact-{Guid.NewGuid():N}@example.com";
        var ownerEmail = $"shipper-owner-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "E2E Shipper Pvt. Ltd.", "E2E Shipper", "01-4444444", contactEmail,
            ownerEmail, password, "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return (merchant!.Id, contactEmail, ownerEmail, password);
    }

    private static CreateShipmentRequest PlainShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private static async Task<JsonElement?> PollMailhogForRecipientAsync(HttpClient httpClient, string recipientEmail, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await httpClient.GetAsync("http://localhost:8025/api/v2/messages");
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            foreach (var item in json.GetProperty("items").EnumerateArray())
            {
                var toArray = item.GetProperty("To");
                if (toArray.GetArrayLength() == 0)
                {
                    continue;
                }
                var mailbox = toArray[0].GetProperty("Mailbox").GetString();
                var domain = toArray[0].GetProperty("Domain").GetString();
                if (string.Equals($"{mailbox}@{domain}", recipientEmail, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            await Task.Delay(500);
        }
        return null;
    }

    private static async Task<T> PollUntilAsync<T>(Func<Task<T?>> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Condition not met within {timeout}.");
    }

    [Fact]
    public async Task ShipmentCreated_SendsRealEmailToMerchantContactEmail()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "email");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var (merchantId, contactEmail, _, _) = await CreateMerchantAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", PlainShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        createResponse.EnsureSuccessStatusCode();
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        using var mailhogClient = new HttpClient();
        var message = await PollMailhogForRecipientAsync(mailhogClient, contactEmail, TimeSpan.FromSeconds(30));

        Assert.NotNull(message);
        var bodyResponse = await mailhogClient.GetAsync($"http://localhost:8025/api/v1/messages/{message!.Value.GetProperty("ID").GetString()}");
        var rawBody = await bodyResponse.Content.ReadAsStringAsync();
        Assert.Contains(shipment!.Awb, rawBody);
    }

    [Fact]
    public async Task ShipmentCreated_ForNepaliTenant_SendsNepaliEmail()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "email-ne");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var profileResponse = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", new { timeZoneId = "Asia/Kathmandu", defaultLocale = "ne" });
        profileResponse.EnsureSuccessStatusCode();
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var (merchantId, contactEmail, _, _) = await CreateMerchantAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", PlainShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        createResponse.EnsureSuccessStatusCode();
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        using var mailhogClient = new HttpClient();
        var message = await PollMailhogForRecipientAsync(mailhogClient, contactEmail, TimeSpan.FromSeconds(30));

        Assert.NotNull(message);
        var bodyResponse = await mailhogClient.GetAsync($"http://localhost:8025/api/v1/messages/{message!.Value.GetProperty("ID").GetString()}");
        var messageJson = await bodyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var content = messageJson.GetProperty("Content");
        var rawBody = content.GetProperty("Body").GetString()!;
        // UTF-8 Nepali text is sent base64 transfer-encoded (the English default is not), so decode first.
        if (content.GetProperty("Headers").TryGetProperty("Content-Transfer-Encoding", out var cte)
            && cte.EnumerateArray().Any(v => string.Equals(v.GetString(), "base64", StringComparison.OrdinalIgnoreCase)))
        {
            rawBody = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(rawBody.Replace("\r", "").Replace("\n", "")));
        }
        Assert.Contains(shipment!.Awb, rawBody);
        Assert.DoesNotContain("has been created and is being processed", rawBody);
        Assert.Contains(rawBody, c => c >= 'ऀ' && c <= 'ॿ'); // Devanagari
    }

    [Fact]
    public async Task WebhookSubscription_ReceivesSignedEvent_ForcedFailureRetriesThenAppearsFailed_ThenReplaySucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "webhook");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var (merchantId, _, ownerEmail, password) = await CreateMerchantAsync(client);

        using var stubTarget = new StubWebhookTarget { ResponseStatusCode = 500 };

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var subscribeResponse = await merchantClient.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest(stubTarget.Url, new[] { "shipment.created" }));
        subscribeResponse.EnsureSuccessStatusCode();
        var subscription = await subscribeResponse.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", PlainShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        createResponse.EnsureSuccessStatusCode();

        // First attempt happens immediately, inline in WebhooksConsumer's message handling — no
        // need to wait for the Hangfire retry job's cron schedule for this first failure.
        var deliveries = await PollUntilAsync(async () =>
        {
            var response = await merchantClient.GetAsync($"/api/v1/webhooks/{subscription!.Id}/deliveries");
            var list = await response.Content.ReadFromJsonAsync<List<WebhookDeliveryResponse>>();
            return list!.Count > 0 ? list : null;
        }, TimeSpan.FromSeconds(30));
        var delivery = Assert.Single(deliveries);
        Assert.Equal("shipment.created", delivery.EventType);
        Assert.Equal(1, delivery.Attempt);
        Assert.Equal(500, delivery.ResponseStatus);
        Assert.Null(delivery.DeliveredAt);
        Assert.NotNull(delivery.NextRetryAt);
        Assert.True(stubTarget.RequestCount >= 1);
        Assert.NotNull(stubTarget.LastSignatureHeader);
        Assert.StartsWith("sha256=", stubTarget.LastSignatureHeader);
        // The delivered payload is the event's own Data (ShipmentId/MerchantId/Awb), not a
        // wrapper carrying the literal event-type string — that only exists as the RabbitMQ
        // routing key and this delivery's own EventType field, already asserted above.
        Assert.NotNull(stubTarget.LastBody);
        Assert.Contains("ShipmentId", stubTarget.LastBody);

        // Fix the target, then replay on demand rather than waiting out the real 1-minute
        // backoff — the backoff schedule's own timing math is already proven by unit tests;
        // this test's job is to prove replay recovers a failed delivery over a real HTTP round
        // trip, not to wait out a cron timer.
        stubTarget.ResponseStatusCode = 200;
        var replayResponse = await merchantClient.PostAsync($"/api/v1/webhooks/deliveries/{delivery.Id}/replay", null);
        replayResponse.EnsureSuccessStatusCode();
        var replayed = await replayResponse.Content.ReadFromJsonAsync<WebhookDeliveryResponse>();

        Assert.NotNull(replayed!.DeliveredAt);
        Assert.Equal(200, replayed.ResponseStatus);
        Assert.Null(replayed.NextRetryAt);
    }
}
