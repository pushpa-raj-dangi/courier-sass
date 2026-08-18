using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();

app.MapGet("/health/ping", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
