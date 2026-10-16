# OrionSearch API

ASP.NET Core backend foundation for ORION-9. It runs independently of PostgreSQL, the web client and the Python research worker.

## Requirements and local setup

Install .NET SDK **10.0.204**. `global.json` permits later patches in the 10.0.2xx feature band, but no prerelease SDKs. Run these commands from `services/api/`:

```sh
dotnet restore
dotnet build
dotnet test
dotnet run --project src/OrionSearch.Api
```

The checked-in launch profile selects Development and listens on `http://localhost:5190`. Stop the API with Ctrl+C. No database, Docker or paid service is needed.

| Endpoint | Response |
| --- | --- |
| `GET /health` | `200` with `{"status":"healthy"}`; process liveness only |
| `GET /openapi/v1.json` | OpenAPI JSON in Development; `404` in other environments |

There is no Swagger UI. The `.http` file beside `Program.cs` contains sample requests. Unknown routes return ProblemDetails; unexpected exceptions return a generic `500` ProblemDetails response with a `traceId`. Exception details stay in the server logs.

## Structure

```text
OrionSearch.sln
global.json
src/
  OrionSearch.Api/
    Configuration/
    Endpoints/
    Extensions/
    Properties/launchSettings.json
    Program.cs
    appsettings.json
    appsettings.Development.json
  OrionSearch.Application/
    Abstractions/ICurrentUser.cs
  OrionSearch.Domain/
  OrionSearch.Infrastructure/
tests/
  OrionSearch.Tests/
    ApiFactory.cs
    ApiTests.cs
    ConfigurationTests.cs
    ReverseProxyTests.cs
```

Api owns HTTP handling and startup. References flow from Api to Application and Infrastructure, from Infrastructure to Application, and from Application to Domain. Domain and Infrastructure currently contain only project files: their implementations belong to later issues. No marker classes or speculative repositories are needed yet.

`ICurrentUser` is the application boundary for a future authenticated identity. Its nullable string ID allows different identity providers. It has no implementation or DI registration yet. Authorization services and middleware are present, but authentication schemes and endpoint policies must be added when the team chooses an authentication provider. The health endpoint is public.

There is no persistent state to initialize or migrate in this phase. ASP.NET Core owns the host and service lifetimes and disposes them on shutdown. Integration tests use `WebApplicationFactory`; their failure and request probes are injected by the test assembly and aren't API endpoints. The Python worker remains a separate service; versioned JSON contracts will be agreed under `packages/contracts/` before integration.

## Configuration

ASP.NET Core loads `appsettings.json`, then `appsettings.{Environment}.json`, Development user secrets, environment variables and command-line arguments, in increasing precedence. Nested environment keys use double underscores. `.env` and `appsettings.Local.json` are **not** loaded automatically. Keep local secrets out of tracked settings; the API project has a user-secrets ID for development.

| Setting | Default / purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Production without a launch profile; the local profile selects Development |
| `ASPNETCORE_URLS` | Set listening addresses when running without a launch profile |
| `Cors__AllowedOrigins__0` (and subsequent indexes) | Exact frontend origins, with no paths or trailing slashes; none outside Development |
| `ReverseProxy__KnownProxies__0` (and subsequent indexes) | Additional trusted proxy IP addresses; empty by default |
| `AllowedHosts` | `*` locally; restrict to deployment hostnames when deploying |
| `Logging__LogLevel__Default` | `Information`; console logs use JSON |

Development allows `http://localhost:3000`. CORS allows methods and headers from configured origins, without credentialed cookie requests. Invalid origins and proxy addresses stop startup with a configuration error. Arrays merge by index across configuration sources, so edit the environment settings file when replacing a list with a shorter one.

For example, to run with Production settings on another port in PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://localhost:5191'
$env:Cors__AllowedOrigins__0 = 'https://web.example'
dotnet run --project src/OrionSearch.Api --no-launch-profile
```

Forwarded headers run before the rest of the pipeline. Only `X-Forwarded-For` and `X-Forwarded-Proto` are accepted, for one proxy hop, from ASP.NET Core's default loopback proxies or explicitly configured IPs. Other peers' forwarded headers are ignored. Before a Railway deployment, establish its proxy addresses, configure trust and hostnames, and bind to its assigned port. TLS termination belongs to that deployment's reverse proxy; local HTTP does not redirect to HTTPS. No Railway deployment or infrastructure is included here.

## Dependencies and tests

The API uses `Microsoft.AspNetCore.OpenApi` 10.0.8 and the ASP.NET Core shared framework. `Microsoft.OpenApi` is explicitly pinned to 2.7.5 to avoid the vulnerable 2.0.0 transitive default ([upstream advisory](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)). Logging, CORS, options validation, authorization and ProblemDetails use built-in framework features.

Tests use xUnit 2.9.3, its Visual Studio runner 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1 and `Microsoft.AspNetCore.Mvc.Testing` 10.0.8. They cover startup without external services, health responses, sanitized errors in Development and Production, missing routes, OpenAPI availability, CORS and configuration validation, and trusted versus untrusted proxy headers.

## Later phases

ORION-10 adds PostgreSQL persistence with EF Core and Npgsql, entities, migrations and database readiness checks. No connection string is needed now. Archive operations, research runs, job processing, AI requests and magazine generation are also deferred.

The following configuration categories are future design decisions, not settings consumed by this API:

| Category | To define when implemented |
| --- | --- |
| PostgreSQL | Connection-string key and secure credentials |
| Authentication | Provider, issuer, audience, schemes and user-context implementation |
| Research service | Endpoint, versioned contracts, authentication and HTTP timeouts |
| Research jobs | Scheduling, retry limits, cancellation and concurrency |

CI/CD, Docker Compose, Railway setup and frontend changes are outside this issue.
