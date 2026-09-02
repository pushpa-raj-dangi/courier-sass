using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Courier.Audit.Application;
using Courier.Audit.Infrastructure;
using Courier.Branches.Application;
using Courier.Branches.Infrastructure;
using Courier.Branches.Presentation;
using Courier.Cod.Application;
using Courier.Cod.Presentation;
using Courier.Events.Application;
using Courier.Events.Infrastructure;
using Courier.Geography.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Infrastructure;
using Courier.Webhooks.Application;
using Courier.Webhooks.Infrastructure;
using Courier.Webhooks.Presentation;
using Courier.Geography.Presentation;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Identity.Presentation;
using Courier.Manifests.Application;
using Courier.Manifests.Presentation;
using Courier.Ndr.Application;
using Courier.Ndr.Presentation;
using Courier.Riders.Application;
using Courier.Riders.Presentation;
using Courier.Merchants.Application;
using Courier.Merchants.Infrastructure;
using Courier.Merchants.Presentation;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Presentation;
using Courier.Reports.Application;
using Courier.Reports.Presentation;
using Courier.Shipments.Application;
using Courier.Shipments.Presentation;
using Courier.SharedKernel;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
// writeToProviders: true — Serilog does not forward events to other ILoggerProvider instances
// registered in DI by default. Without this, ILogger<T> calls (e.g.
// ApiKeyRequestLoggingMiddleware, Task 13) would be invisible to any ILoggerProvider a test host
// or future OpenTelemetry logging exporter registers via AddSingleton<ILoggerProvider>, even
// though they still reach the Console sink either way. No effect on production output today,
// since no other ILoggerProvider is currently registered there.
// preserveStaticLogger: true — without this, UseSerilog also overwrites the process-wide static
// Serilog.Log.Logger as a side effect of every host build. WebApplicationFactory-based tests
// build many hosts (often concurrently, one per test class), and each host's default UseSerilog
// call would otherwise race to clobber the same static field — causing a request handled by one
// test host's pipeline to be logged through a DIFFERENT host's (or no host's) writeToProviders
// wiring. Keeping each host's logger private to its own DI container avoids that cross-host race.
//
// CAUTION: app.UseSerilogRequestLogging() below (RequestLoggingOptions.Logger left null) falls
// back to the static Log.Logger by default (Serilog.AspNetCore's documented behavior). With
// preserveStaticLogger: true, that static field is never assigned, so it would silently stay at
// Serilog's no-op default and swallow every request-completion log — this is NOT a test-only
// concern, it would break production output too. The fix is at the UseSerilogRequestLogging call
// site itself: pass this host's real Serilog.ILogger (resolved from DI) via
// RequestLoggingOptions.Logger, rather than relying on the static field at all. See the comment
// there.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(),
    writeToProviders: true,
    preserveStaticLogger: true);

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis")
    .AddRabbitMQ(async _ =>
    {
        var factory = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMq")!) };
        return await factory.CreateConnectionAsync();
    }, name: "rabbitmq");

// One shared connection for the whole process — every publisher/consumer opens its own
// channel from this (channels are cheap and not thread-safe to share; connections are
// expensive and safe to share). Registered as a factory-created singleton so Program.cs's own
// startup path doesn't block on an async connection open before the host is ready to serve
// health checks — the first real user of the connection (Task 4's publisher job, or either
// consumer) is the first thing that actually needs it live.
builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
{
    var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMq")!) };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// First real (non-health-check) use of Redis in this codebase — backs the new
// IRateLimiter/IIdempotencyStore (Phase 12). Registered as a singleton the same way the
// RabbitMQ connection above is: ConnectionMultiplexer is thread-safe and expensive to open,
// so the whole process shares one.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres"))));
builder.Services.AddHangfireServer();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<MerchantJwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<UserManagementService>();
builder.Services.AddScoped<IPermissionChecker, Courier.Identity.Infrastructure.PermissionChecker>();
builder.Services.AddScoped<IOrganizationLookupService, OrganizationLookupService>();
builder.Services.AddScoped<IBranchHierarchyService, BranchHierarchyService>();
builder.Services.AddScoped<IBranchAuthorizationChecker, Courier.Identity.Infrastructure.BranchAuthorizationChecker>();
builder.Services.AddScoped<BranchService>();
builder.Services.AddScoped<IBranchLookupService>(sp => sp.GetRequiredService<BranchService>());
builder.Services.AddScoped<RateCardService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<IShipmentTransitionService>(sp => sp.GetRequiredService<ShipmentService>());
builder.Services.AddScoped<IShipmentTaskLookupService>(sp => sp.GetRequiredService<ShipmentService>());
builder.Services.AddScoped<PublicTrackingService>();
builder.Services.AddScoped<PickupRequestService>();
builder.Services.AddScoped<ManifestService>();
builder.Services.AddScoped<RiderService>();
builder.Services.AddScoped<INdrCreationService, NdrCreationService>();
builder.Services.AddScoped<CodService>();
builder.Services.AddScoped<ICodCollectionCreationService>(sp => sp.GetRequiredService<CodService>());
builder.Services.AddScoped<ICodCollectionService>(sp => sp.GetRequiredService<CodService>());
builder.Services.AddScoped<IOutboxWriter, OutboxWriterService>();
builder.Services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddScoped<OutboxPublisherJob>();
builder.Services.AddScoped<NdrService>();
builder.Services.AddScoped<IShipmentReportingLookupService, ShipmentReportingLookupService>();
builder.Services.AddScoped<IManifestReportingLookupService, ManifestReportingLookupService>();
builder.Services.AddScoped<IRiderReportingLookupService, RiderReportingLookupService>();
builder.Services.AddScoped<INdrReportingLookupService, NdrReportingLookupService>();
builder.Services.AddScoped<ICodReportingLookupService, CodReportingLookupService>();
builder.Services.AddScoped<OrgDashboardService>();
builder.Services.AddScoped<BranchDashboardService>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<GeographyQueryService>();
builder.Services.AddScoped<IGeographyLookupService>(sp => sp.GetRequiredService<GeographyQueryService>());
builder.Services.AddScoped<DeliveryZoneService>();
builder.Services.AddScoped<IMerchantPasswordHasher, MerchantPasswordHasherAdapter>();
builder.Services.AddScoped<IMerchantJwtTokenService, MerchantJwtTokenService>();
builder.Services.AddScoped<IMerchantAuthorizationChecker, MerchantAuthorizationChecker>();
builder.Services.AddScoped<MerchantAuthService>();
builder.Services.AddScoped<MerchantOnboardingService>();
builder.Services.AddScoped<MerchantStaffService>();
builder.Services.AddScoped<MerchantKycService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<MerchantLookupService>();
builder.Services.AddScoped<IMerchantLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());
builder.Services.AddScoped<IMerchantContactLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, SmsNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, WhatsAppNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, PushNotificationChannel>();
builder.Services.AddScoped<NotificationDispatchService>();
builder.Services.AddHostedService<NotificationsConsumer>();
// Keys persisted to Postgres (via AppDbContext) so the key ring survives container
// restarts/recreates — without this, ciphertext produced before a restart (e.g. webhook
// secrets) becomes permanently undecryptable once the in-container default key store is wiped.
builder.Services.AddDataProtection()
    .SetApplicationName("Courier")
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddHttpClient("Webhooks", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IWebhookDeliveryAttemptService, WebhookDeliveryAttemptService>();
builder.Services.AddHostedService<WebhooksConsumer>();
builder.Services.AddScoped<WebhookRetryJob>();
builder.Services.AddSingleton<IObjectStorage>(_ =>
    new LocalFileSystemObjectStorage(builder.Configuration["ObjectStorage:LocalRootPath"] ?? Path.Combine(AppContext.BaseDirectory, "storage")));

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core's default inbound claim map silently rewrites
        // well-known JWT claim names (e.g. "sub" -> ClaimTypes.NameIdentifier) on the
        // ClaimsPrincipal built from the token, which breaks any code (PermissionChecker,
        // audit logging, future endpoints) that reads HttpContext.User.FindFirst("sub")
        // directly. Disabling it keeps claims exactly as JwtTokenService issued them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Integration tests spin up multiple WebApplicationFactory<Program> instances in
    // parallel (xUnit runs different test classes concurrently by default), and each
    // one re-executes this Main. Against a fresh database, unsynchronized concurrent
    // Migrate() calls race and fail with "relation already exists". This lock is a
    // static field on the partial Program class (not a top-level local), so it's
    // shared across every Main invocation within the same test process.
    lock (Program.MigrationLock)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Courier API v1");
        options.RoutePrefix = "swagger";
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
    app.UseHangfireDashboard("/hangfire");
}

// Forces the Data Protection key ring to materialize now (reading/creating its key row in
// Postgres via PersistKeysToDbContext) rather than lazily on the first real Protect/Unprotect
// call. Without this, that first call pays a one-time DB round trip inline wherever it happens
// to occur first — e.g. WebhooksConsumer's synchronous first-delivery-attempt path — which
// widens the pre-existing window between WebhooksConsumer's initial WebhookDelivery insert and
// its follow-up attempt update (Task 10) enough to be observed by a caller polling in between.
using (var warmupScope = app.Services.CreateScope())
{
    warmupScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("Webhooks.Secret")
        .Protect("startup-warmup");
}

// Explicit Logger, not the default (options.Logger left null, which falls back to the static
// Serilog.Log.Logger): UseSerilog(...) above is configured with preserveStaticLogger: true, so it
// no longer assigns Log.Logger as a side effect — that static field stays at Serilog's silent
// no-op default. Resolving this host's own Serilog.ILogger from DI (registered by the 3-arg
// UseSerilog(...) overload's internal AddSerilog call) and passing it explicitly keeps
// UseSerilogRequestLogging's per-request completion log wired to the real configured logger
// regardless of the static field's state.
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
});
app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Courier.Api.Middleware.ApiKeyRequestLoggingMiddleware>();
app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapRolesEndpoints();
app.MapBranchesEndpoints();
app.MapGeographyEndpoints();
app.MapDeliveryZonesEndpoints();
app.MapMerchantsEndpoints();
app.MapMerchantAuthEndpoints();
app.MapMerchantProfileEndpoints();
app.MapMerchantStaffEndpoints();
app.MapApiKeyEndpoints();
app.MapMerchantKycEndpoints();
app.MapPricingEndpoints();
app.MapShipmentsEndpoints();
app.MapPublicTrackingEndpoints();
app.MapPickupsEndpoints();
app.MapManifestsEndpoints();
app.MapRidersEndpoints();
app.MapNdrEndpoints();
app.MapCodEndpoints();
app.MapReportsEndpoints();
app.MapWebhooksEndpoints();

app.MapGet("/health/ping", () => Results.Ok(new { status = "ok" })).WithTags("Health check");

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

RecurringJob.AddOrUpdate<OutboxPublisherJob>("outbox-publisher", job => job.RunAsync(CancellationToken.None), "*/10 * * * * *");
RecurringJob.AddOrUpdate<WebhookRetryJob>("webhook-retry", job => job.RunAsync(CancellationToken.None), "*/30 * * * * *");

app.Run();

public partial class Program
{
    private static readonly object MigrationLock = new();
}
