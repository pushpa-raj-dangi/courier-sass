using System.Text;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Audit.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis")
    .AddRabbitMQ(async _ =>
    {
        var factory = new ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMq")!) };
        return await factory.CreateConnectionAsync();
    }, name: "rabbitmq");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IPermissionChecker, Courier.Identity.Infrastructure.PermissionChecker>();

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
    });
builder.Services.AddAuthorization();

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
}

app.UseSerilogRequestLogging();
app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUsersEndpoints();

app.MapGet("/health/ping", () => Results.Ok(new { status = "ok" }));

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

app.Run();

public partial class Program
{
    private static readonly object MigrationLock = new();
}
