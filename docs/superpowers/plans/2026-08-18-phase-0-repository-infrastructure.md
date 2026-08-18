# Phase 0 — Repository & Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the repository skeleton — ASP.NET Core API host, Next.js web host,
Docker Compose (Postgres/Redis/RabbitMQ/api/web), structured logging, global error
handling, and CI — with no business modules yet, per `docs/ROADMAP.md` Phase 0.

**Architecture:** Modular monolith per `docs/ARCHITECTURE.md`. Phase 0 only builds the
`src/Api` presentation host and `src/Web` Next.js app plus cross-cutting
logging/error-handling — no `Modules/` or `SharedKernel` yet, since nothing needs them
until Phase 1's Identity module. Docker Compose wires the host services together;
GitHub Actions proves the whole stack builds and tests green with real service
containers, not mocks.

**Tech Stack:** ASP.NET Core (.NET 8) minimal API, Serilog, ASP.NET Core Health
Checks (Npgsql/Redis/RabbitMQ), xUnit, Next.js 14 (App Router) + TypeScript +
Tailwind CSS + shadcn/ui + TanStack Query, Vitest + React Testing Library, Docker
Compose, GitHub Actions.

**Spec:** `docs/ARCHITECTURE.md`, `docs/ROADMAP.md` (Phase 0 section), `docs/API-SPEC.md`
(§2 error envelope format).

## Global Constraints

- Modular monolith, no microservices (CLAUDE.md §2).
- No business logic in this phase — Phase 0 is infrastructure only (`docs/ROADMAP.md`).
- Error responses use the exact envelope shape from `docs/API-SPEC.md` §2:
  `{ "error": { "code", "message", "details", "traceId" } }`.
- Every tenant/business concern (EF Core, RBAC, etc.) is explicitly deferred to
  Phase 1+ — do not add it here even if convenient.
- Connection string config keys (`ConnectionStrings:Postgres` / `:Redis` /
  `:RabbitMq`) must stay identical across `appsettings.Development.json`,
  `docker-compose.yml`, and `.github/workflows/ci.yml` — a mismatch here is the
  most likely single point of failure for the Phase 0 exit criteria.
- Commit after every task using conventional prefixes (`feat:`, `chore:`, `ci:`).

---

### Task 1: Backend Solution Scaffold + `/health/ping`

**Files:**
- Create: `.gitignore`
- Create: `Courier.sln`
- Create: `src/Api/Api.csproj`
- Create: `src/Api/Program.cs`
- Create: `tests/Api.Tests/Api.Tests.csproj`
- Create: `tests/Api.Tests/HealthPingTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: a running ASP.NET Core host exposing `GET /health/ping` → `200 OK
  { "status": "ok" }`. `Program` is `public partial class` so later test projects can
  reference it via `WebApplicationFactory<Program>`.

- [ ] **Step 1: Initialize the git repository and add `.gitignore`**

```bash
git init
```

`.gitignore`:

```gitignore
# .NET
bin/
obj/
*.user

# Node / Next.js
node_modules/
.next/
out/

# Environment
.env
.env.local

# IDE
.vs/
.vscode/
.idea/
```

- [ ] **Step 2: Scaffold the solution and API project (no endpoint yet)**

```bash
dotnet new sln -n Courier
dotnet new web -n Api -o src/Api
dotnet sln add src/Api/Api.csproj
```

Replace the generated `src/Api/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();

public partial class Program { }
```

- [ ] **Step 3: Scaffold the test project**

```bash
dotnet new xunit -n Api.Tests -o tests/Api.Tests
dotnet sln add tests/Api.Tests/Api.Tests.csproj
dotnet add tests/Api.Tests/Api.Tests.csproj reference src/Api/Api.csproj
dotnet add tests/Api.Tests/Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 8.0.10
```

- [ ] **Step 4: Write the failing test**

`tests/Api.Tests/HealthPingTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class HealthPingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthPingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_HealthPing_Returns200Ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: FAIL — `404 NotFound` (endpoint doesn't exist yet).

- [ ] **Step 6: Implement the endpoint**

In `src/Api/Program.cs`, insert before `app.Run();`:

```csharp
app.MapGet("/health/ping", () => Results.Ok(new { status = "ok" }));
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add .gitignore Courier.sln src/Api tests/Api.Tests
git commit -m "feat: scaffold API host with /health/ping"
```

---

### Task 2: Global Exception Handling Middleware

**Files:**
- Create: `src/Api/Middleware/ExceptionHandlingMiddleware.cs`
- Create: `tests/Api.Tests/ExceptionHandlingMiddlewareTests.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ExceptionHandlingMiddleware` — an ASP.NET Core middleware class with
  `InvokeAsync(HttpContext)` that catches unhandled exceptions and writes the
  `docs/API-SPEC.md` §2 error envelope with HTTP 500. Later tasks/phases that throw
  domain exceptions rely on this envelope shape being stable.

- [ ] **Step 1: Write the failing test**

`tests/Api.Tests/ExceptionHandlingMiddlewareTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Courier.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courier.Api.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNextThrows_Writes500ErrorEnvelope()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("error");

        Assert.Equal("INTERNAL_SERVER_ERROR", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("traceId").GetString()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: FAIL — compile error, `ExceptionHandlingMiddleware` doesn't exist yet.

- [ ] **Step 3: Implement the middleware**

`src/Api/Middleware/ExceptionHandlingMiddleware.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Courier.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Path}", context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var envelope = new
            {
                error = new
                {
                    code = "INTERNAL_SERVER_ERROR",
                    message = "An unexpected error occurred.",
                    details = (object?)null,
                    traceId = context.TraceIdentifier
                }
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(envelope));
        }
    }
}
```

- [ ] **Step 4: Wire the middleware into the pipeline**

In `src/Api/Program.cs`, insert immediately after `var app = builder.Build();` and
before any `app.Map...` calls:

```csharp
app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS (both `HealthPingTests` and `ExceptionHandlingMiddlewareTests`)

- [ ] **Step 6: Commit**

```bash
git add src/Api/Middleware src/Api/Program.cs tests/Api.Tests/ExceptionHandlingMiddlewareTests.cs
git commit -m "feat: add global exception handling middleware with standard error envelope"
```

---

### Task 3: Serilog Structured Logging

**Files:**
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: Serilog console request logging active on every request; no new public
  API surface for other tasks to consume.

- [ ] **Step 1: Add Serilog packages**

```bash
dotnet add src/Api/Api.csproj package Serilog.AspNetCore --version 8.0.1
```

(`Serilog.AspNetCore` transitively brings in `Serilog.Sinks.Console`.)

- [ ] **Step 2: Configure Serilog in `Program.cs`**

Replace the top of `src/Api/Program.cs` with:

```csharp
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
```

(`app.UseSerilogRequestLogging()` must come before the exception middleware so it
still logs the request even when the exception middleware handles a failure; Serilog
request logging itself already captures unhandled exceptions that escape further
down, so ordering here is intentional, not arbitrary.)

- [ ] **Step 3: Run existing tests to confirm no regression**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS (unchanged — this task adds no new automated test because console log
output is not meaningfully unit-testable; verified manually in Step 4 instead)

- [ ] **Step 4: Manually verify structured log output**

Run: `dotnet run --project src/Api/Api.csproj`, then in another terminal:
`curl http://localhost:5000/health/ping`
Expected: the API's console output includes a request-completion log line (e.g.
`HTTP GET /health/ping responded 200 in ...ms`).

- [ ] **Step 5: Commit**

```bash
git add src/Api/Api.csproj src/Api/Program.cs
git commit -m "feat: wire Serilog structured console logging and request logging"
```

---

### Task 4: Dependency Health Checks (Postgres/Redis/RabbitMQ)

**Files:**
- Create: `src/Api/appsettings.json`
- Create: `src/Api/appsettings.Development.json`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Create: `tests/Api.Tests/HealthReadyTests.cs`

**Interfaces:**
- Consumes: `ConnectionStrings:Postgres`, `ConnectionStrings:Redis`,
  `ConnectionStrings:RabbitMq` from configuration.
- Produces: `GET /health/ready` → `200` with
  `{ "status": "Healthy", "checks": [{ "name": "postgres", "status": "Healthy" }, ...] }`
  when all three dependencies are reachable, non-200 otherwise. Task 5 (Docker
  Compose) and Task 7 (CI) both depend on this endpoint existing with this exact
  shape to prove the stack is wired correctly.

- [ ] **Step 1: Add health check packages**

```bash
dotnet add src/Api/Api.csproj package AspNetCore.HealthChecks.NpgSql --version 8.0.1
dotnet add src/Api/Api.csproj package AspNetCore.HealthChecks.Redis --version 8.0.1
dotnet add src/Api/Api.csproj package AspNetCore.HealthChecks.Rabbitmq --version 8.0.1
```

Note: `AspNetCore.HealthChecks.Rabbitmq`'s `AddRabbitMQ(...)` overload has changed
shape across versions as `RabbitMQ.Client` moved to an async connection API — if the
installed version's overload doesn't match Step 3 below, use whatever overload
NuGet/IntelliSense shows for connecting via a connection string or URI; the endpoint
contract in this task (`/health/ready` shape) is what matters, not the exact overload
name.

- [ ] **Step 2: Add connection string configuration**

`src/Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

`src/Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=courier;Username=courier;Password=courier_dev_password",
    "Redis": "localhost:6379",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  }
}
```

- [ ] **Step 3: Register health checks and map the endpoint**

In `src/Api/Program.cs`, after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis")
    .AddRabbitMQ(builder.Configuration.GetConnectionString("RabbitMq")!, name: "rabbitmq");
```

After `app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();`, add:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
```

(Move the `using` statements to the top of the file with the others.)

- [ ] **Step 4: Write the test**

`tests/Api.Tests/HealthReadyTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class HealthReadyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthReadyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_HealthReady_ReturnsHealthy_WhenDependenciesAreUp()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

This test requires Postgres/Redis/RabbitMQ reachable at the
`appsettings.Development.json` connection strings above.

- [ ] **Step 5: Run test to verify it fails (no services running yet)**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter HealthReadyTests`
Expected: FAIL (connection refused) — Task 5 hasn't stood up the containers yet.
This is expected and fine; proceed to Task 5, then return and confirm PASS per
Task 5's verification step.

- [ ] **Step 6: Commit**

```bash
git add src/Api/appsettings.json src/Api/appsettings.Development.json src/Api/Api.csproj src/Api/Program.cs tests/Api.Tests/HealthReadyTests.cs
git commit -m "feat: add Postgres/Redis/RabbitMQ health checks at /health/ready"
```

---

### Task 5: Docker Compose (Postgres, Redis, RabbitMQ, API, Web)

**Files:**
- Create: `src/Api/Dockerfile`
- Create: `docker-compose.yml`

**Interfaces:**
- Consumes: `Api.csproj` (Task 1), `/health/ready` (Task 4). The `web` service here
  references `src/Web/Dockerfile`, which does not exist until Task 6 — this task's
  verification step therefore only brings up `postgres`, `redis`, `rabbitmq`, `api`;
  full-stack `docker compose up` (including `web`) is verified again at the end of
  Task 6.
- Produces: `docker-compose.yml` at the repo root with services `postgres`, `redis`,
  `rabbitmq`, `api`, `web`, and a `postgres-data` named volume. Later phases add
  migrations/seed steps on top of this file — they must not change the service
  names or the `ConnectionStrings__*` env var keys, since Task 4 and Task 7 both
  depend on them.

- [ ] **Step 1: Write the API Dockerfile**

`src/Api/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
```

- [ ] **Step 2: Write `docker-compose.yml`**

`docker-compose.yml` (repo root):

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: courier
      POSTGRES_PASSWORD: courier_dev_password
      POSTGRES_DB: courier
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U courier"]
      interval: 5s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 5s
      retries: 5

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 5s
      retries: 5

  api:
    build:
      context: .
      dockerfile: src/Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Port=5432;Database=courier;Username=courier;Password=courier_dev_password"
      ConnectionStrings__Redis: "redis:6379"
      ConnectionStrings__RabbitMq: "amqp://guest:guest@rabbitmq:5672"
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy

  web:
    build:
      context: src/Web
      dockerfile: Dockerfile
    ports:
      - "3000:3000"
    environment:
      NEXT_PUBLIC_API_BASE_URL: "http://localhost:5000"
    depends_on:
      - api

volumes:
  postgres-data:
```

- [ ] **Step 3: Validate and bring up the backend services**

```bash
docker compose config
docker compose up -d postgres redis rabbitmq api
```

Expected: `docker compose config` prints the resolved config with no errors;
`docker compose up` reports all four containers healthy/running.

- [ ] **Step 4: Verify `/health/ready` against the real containers**

```bash
curl http://localhost:5000/health/ready
```

Expected: `200` with `"status":"Healthy"` and all three checks `"Healthy"`. This is
also the moment to go back and re-run Task 4's `HealthReadyTests` — it should now
PASS if run with `ASPNETCORE_ENVIRONMENT=Development` (or against the containers via
their host-exposed ports, which match `appsettings.Development.json`).

- [ ] **Step 5: Tear down**

```bash
docker compose down
```

- [ ] **Step 6: Commit**

```bash
git add src/Api/Dockerfile docker-compose.yml
git commit -m "chore: add Docker Compose for postgres/redis/rabbitmq/api/web"
```

---

### Task 6: Next.js Web Skeleton

**Files:**
- Create: `src/Web/package.json` (via `create-next-app`, then edited)
- Create: `src/Web/next.config.js`
- Create: `src/Web/tailwind.config.ts`
- Create: `src/Web/src/app/layout.tsx`
- Create: `src/Web/src/app/page.tsx`
- Create: `src/Web/src/app/providers.tsx`
- Create: `src/Web/vitest.config.ts`
- Create: `src/Web/vitest.setup.ts`
- Create: `src/Web/src/app/page.test.tsx`
- Create: `src/Web/Dockerfile`

**Interfaces:**
- Consumes: nothing from the backend yet (no API calls in Phase 0).
- Produces: a Next.js app served at `http://localhost:3000` rendering a "Courier
  Platform" homepage, wrapped in a `Providers` component that later features
  (`docs/ARCHITECTURE.md` §4 feature folders) will rely on for `QueryClientProvider`.

- [ ] **Step 1: Scaffold the Next.js app**

```bash
npx create-next-app@14 src/Web --typescript --tailwind --app --src-dir --import-alias "@/*" --eslint --use-npm
```

This produces the default Next.js boilerplate homepage (which still says "Get
started by editing...", not "Courier Platform") — that mismatch is what Step 4's
test below exploits to fail first.

- [ ] **Step 2: Install additional runtime and test dependencies**

```bash
cd src/Web
npm install @tanstack/react-query react-hook-form zod
npm install -D vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/jest-dom
cd ../..
```

- [ ] **Step 3: Configure Vitest**

`src/Web/vitest.config.ts`:

```ts
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    globals: true,
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
});
```

`src/Web/vitest.setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
```

Add to `src/Web/package.json` `"scripts"`:

```json
"test": "vitest run"
```

- [ ] **Step 4: Write the failing test**

`src/Web/src/app/page.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import HomePage from "./page";

describe("HomePage", () => {
  it("renders the platform heading", () => {
    render(<HomePage />);
    expect(
      screen.getByRole("heading", { name: /courier platform/i })
    ).toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run test to verify it fails**

Run: `cd src/Web && npm run test`
Expected: FAIL — no "Courier Platform" heading in the default scaffolded page.

- [ ] **Step 6: Implement the homepage and providers**

`src/Web/src/app/providers.tsx`:

```tsx
"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient());

  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}
```

`src/Web/src/app/layout.tsx` (replace the generated file, keeping its font/metadata
setup but wrapping children):

```tsx
import type { Metadata } from "next";
import { Inter } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";

const inter = Inter({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Courier Platform",
  description: "Nepal domestic courier operations platform",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className={inter.className}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
```

`src/Web/src/app/page.tsx` (replace the generated file):

```tsx
export default function HomePage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center p-24">
      <h1 className="text-4xl font-bold">Courier Platform</h1>
      <p className="mt-4 text-muted-foreground">
        Nepal domestic courier operations platform.
      </p>
    </main>
  );
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `cd src/Web && npm run test`
Expected: PASS

- [ ] **Step 8: Configure standalone build output for Docker**

In `src/Web/next.config.js`, add `output: "standalone"` to the config object.

- [ ] **Step 9: Write the Web Dockerfile**

`src/Web/Dockerfile`:

```dockerfile
FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:20-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app/public ./public
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
EXPOSE 3000
CMD ["node", "server.js"]
```

- [ ] **Step 10: Verify the full stack via Docker Compose**

```bash
docker compose up -d
curl http://localhost:5000/health/ready
curl -I http://localhost:3000
docker compose down
```

Expected: `/health/ready` returns `200`/`Healthy`; the `web` root responds `200`.
This is the Phase 0 exit criterion from `docs/ROADMAP.md` ("`docker compose up`
yields a running API + Web that talk to Postgres/Redis/RabbitMQ").

- [ ] **Step 11: Commit**

```bash
git add src/Web docker-compose.yml
git commit -m "feat: scaffold Next.js web app with TanStack Query provider and Docker build"
```

---

### Task 7: GitHub Actions CI

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `Courier.sln` (Task 1), `src/Web/package.json` (Task 6), the
  `ConnectionStrings__*` env var keys established in Task 5.
- Produces: a green GitHub Actions run on push/PR — the other Phase 0 exit criterion
  from `docs/ROADMAP.md` ("empty but real CI (build + test) green").

- [ ] **Step 1: Write the workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  api:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_USER: courier
          POSTGRES_PASSWORD: courier_dev_password
          POSTGRES_DB: courier
        ports: ["5432:5432"]
        options: >-
          --health-cmd "pg_isready -U courier"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 5
      redis:
        image: redis:7
        ports: ["6379:6379"]
        options: >-
          --health-cmd "redis-cli ping"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 5
      rabbitmq:
        image: rabbitmq:3-management
        ports: ["5672:5672"]
        options: >-
          --health-cmd "rabbitmq-diagnostics -q ping"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 5
    env:
      ConnectionStrings__Postgres: "Host=localhost;Port=5432;Database=courier;Username=courier;Password=courier_dev_password"
      ConnectionStrings__Redis: "localhost:6379"
      ConnectionStrings__RabbitMq: "amqp://guest:guest@localhost:5672"
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - run: dotnet restore Courier.sln
      - run: dotnet build Courier.sln --no-restore --configuration Release
      - run: dotnet test Courier.sln --no-build --configuration Release

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/Web
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "20"
          cache: "npm"
          cache-dependency-path: src/Web/package-lock.json
      - run: npm ci
      - run: npm run lint
      - run: npm run test
      - run: npm run build
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add GitHub Actions workflow for api and web build/test"
```

- [ ] **Step 3: Verify on GitHub**

Push the branch (or open a PR) and confirm both the `api` and `web` jobs show green
in the Actions tab. This is a real CI run, not locally reproducible in full (GitHub
Actions service containers), so it is the one step in this plan verified outside the
local machine — do not mark this task done until the Actions run is actually green.

---

## Self-Review Notes

- **Spec coverage:** Repository (Task 1), Docker/Docker Compose (Tasks 5–6),
  PostgreSQL/Redis/RabbitMQ (Task 4 health checks + Task 5 containers), API (Task 1),
  Web (Task 6), Logging (Task 3), Error handling (Task 2) — all nine `ROADMAP.md`
  Phase 0 bullets are covered. Both Phase 0 exit criteria (`docker compose up` works,
  CI green) are explicit verification steps (Task 6 Step 10, Task 7 Step 3).
- **No placeholders:** every step above contains real, complete file contents, not
  descriptions of them.
- **Type/config consistency:** `ConnectionStrings:Postgres/Redis/RabbitMq` config
  keys and the `postgres`/`redis`/`rabbitmq`/`api`/`web` service names are identical
  across Task 4 (`appsettings.Development.json`), Task 5 (`docker-compose.yml`), and
  Task 7 (`ci.yml`) — checked during self-review, not left to drift.
