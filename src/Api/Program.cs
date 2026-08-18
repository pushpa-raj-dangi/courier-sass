var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();

app.MapGet("/health/ping", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
