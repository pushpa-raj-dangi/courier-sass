using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 13: ApiKeyRequestLoggingMiddleware logs one structured Serilog entry per
// ApiKey-authenticated request and stays silent for every other caller (staff JWT here; merchant
// JWT and anonymous callers are the same "no api_key_scopes claim" code path). Mirrors the
// merchant-registration/API-key-creation helpers from ApiKeyAuthenticationTests (Task 5) and the
// staff-registration helper from ShipmentsEndpointsTests.
file class CapturingLogger : ILogger
{
    private readonly ConcurrentBag<string> _messages;
    public CapturingLogger(ConcurrentBag<string> messages) => _messages = messages;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _messages.Add(formatter(state, exception));
}

file class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
    public void Dispose() { }
}

public class ApiKeyRequestLoggingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ApiKeyRequestLoggingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new RegisterOrganizationRequest(
            $"ApiKeyLog Org {suffix}", $"apikeylog-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    [Fact]
    public async Task ApiKeyAuthenticatedRequest_LogsOneStructuredEntry()
    {
        var capturingProvider = new CapturingLoggerProvider();
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(capturingProvider)));
        var client = scopedFactory.CreateClient();

        // Reuse the merchant-registration + API-key-creation helper pattern from
        // ApiKeyAuthenticationTests (Task 5) to obtain a valid plaintext key, then:
        // client.DefaultRequestHeaders.Add("X-Api-Key", plaintextKey);
        // await client.GetAsync("/api/v1/shipments");
        var staffClient = scopedFactory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "reqlog");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, "reqlog");

        var ownerClient = scopedFactory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);
        await client.GetAsync("/api/v1/shipments");

        Assert.Contains(capturingProvider.Messages, m => m.Contains("ApiKey request") && m.Contains("/api/v1/shipments"));
    }

    [Fact]
    public async Task StaffJwtRequest_LogsNothing()
    {
        var capturingProvider = new CapturingLoggerProvider();
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(capturingProvider)));
        var client = scopedFactory.CreateClient();

        // Register + log in as staff (existing helper pattern), call any authenticated route with
        // the staff JWT, then assert no "ApiKey request" entry was captured — proving the
        // self-selecting claim check actually gates this middleware.
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(client, "reqlogstaff");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var response = await client.GetAsync("/api/v1/shipments");
        Assert.True(response.IsSuccessStatusCode);

        // Prove the capture pipeline itself is live in this run (not vacuously empty, e.g. from a
        // broken ILoggerProvider wiring) before asserting on absence. Verified experimentally
        // (temporarily reverting Program.cs's explicit RequestLoggingOptions.Logger wiring and
        // re-running): this Assert.NotEmpty stays true either way, because ASP.NET Core's own
        // built-in framework diagnostics (e.g. "Request finished ...", "Executed endpoint ...",
        // emitted directly through the DI ILoggerFactory/ILoggerProvider pipeline, independent of
        // Serilog's request-logging middleware or the static Log.Logger) reach this provider
        // regardless. It does NOT specifically prove UseSerilogRequestLogging's own completion log
        // reached this provider — NonApiKeyRequest_StillProducesRequestCompletionLog below is the
        // test that asserts on that specific entry, and is the one that actually regresses (fails)
        // if RequestLoggingOptions.Logger's explicit wiring in Program.cs is removed.
        Assert.NotEmpty(capturingProvider.Messages);
        Assert.DoesNotContain(capturingProvider.Messages, m => m.Contains("ApiKey request"));
    }

    // Regression coverage for the bug the coordinator's reviewer caught: Program.cs's
    // UseSerilog(..., preserveStaticLogger: true) stops Serilog from assigning the static
    // Serilog.Log.Logger as a side effect. app.UseSerilogRequestLogging() (RequestLoggingOptions
    // .Logger left null) falls back to that static field by default per Serilog.AspNetCore's
    // documented behavior — so without explicitly wiring RequestLoggingOptions.Logger to this
    // host's real Serilog.ILogger (see Program.cs), request-completion logging would silently
    // stop reaching Console (and any ILoggerProvider) in production, not just under test
    // parallelism. This test fails if that wiring regresses: it asserts specifically on
    // UseSerilogRequestLogging's own completion-log content (method/path/"responded"/status code),
    // distinct from ApiKeyRequestLoggingMiddleware's own "ApiKey request" log which this request
    // (a staff JWT caller) never triggers.
    [Fact]
    public async Task NonApiKeyRequest_StillProducesRequestCompletionLog()
    {
        var capturingProvider = new CapturingLoggerProvider();
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(capturingProvider)));
        var client = scopedFactory.CreateClient();

        var staffToken = await RegisterOrgAndGetStaffTokenAsync(client, "reqlogcompletion");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var response = await client.GetAsync("/api/v1/shipments");
        Assert.True(response.IsSuccessStatusCode);

        Assert.Contains(capturingProvider.Messages, m =>
            m.Contains("GET") && m.Contains("/api/v1/shipments") && m.Contains("responded") && m.Contains("200"));
    }
}
