using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();

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

public partial class Program { }
