# Build contract

Binding conventions for every layer of this solution. The authoritative type
definitions are the source files themselves — read them before writing code:

- `src/Cadence.Domain/**` — entities and analytics (complete, do not modify)
- `src/Cadence.Application/Common/Result.cs` — `Result<T>`, `Result`, `Error`, `ErrorKind`
- `src/Cadence.Application/Abstractions/Ports.cs` — every interface the infrastructure implements
- `src/Cadence.Application/Contracts/Dtos.cs` — the wire contract

## Non-negotiables

- **.NET 10** (`net10.0`), C# latest, `Nullable=enable`, `ImplicitUsings=enable`,
  **`TreatWarningsAsErrors=true`**. A nullable warning fails the build.
- **Central package management.** Versions live only in `Directory.Packages.props`.
  Add `<PackageReference Include="X" />` with **no** `Version` attribute. If you
  need a package that is not already pinned, say so in your report instead of
  adding a version.
- **Do not modify** `Directory.Build.props`, `Directory.Packages.props`,
  `Cadence.slnx`, any `.csproj`, or anything under `src/Cadence.Domain/` or the
  two Application files listed above.
- Write only the files you are told you own. Another agent owns the rest.
- **Do not run `dotnet build` or `dotnet test`.** Other agents are editing the
  same solution concurrently and the build outputs collide. Integration builds
  happen after every agent has finished.

## Namespaces

| Location | Namespace |
| --- | --- |
| `src/Cadence.Domain/Activities` | `Cadence.Domain.Activities` |
| `src/Cadence.Domain/Analytics` | `Cadence.Domain.Analytics` |
| `src/Cadence.Domain/Athletes` | `Cadence.Domain.Athletes` |
| `src/Cadence.Domain/Coaching` | `Cadence.Domain.Coaching` |
| `src/Cadence.Domain/Geo` | `Cadence.Domain.Geo` |
| `src/Cadence.Application/<Folder>` | `Cadence.Application.<Folder>` |
| `src/Cadence.Infrastructure/<Folder>` | `Cadence.Infrastructure.<Folder>` |
| `src/Cadence.Api/<Folder>` | `Cadence.Api.<Folder>` |

File-scoped namespace declarations (`namespace X;`) everywhere.

## Dependency-injection entry points

Each area exposes exactly one extension method. These names are fixed because
other agents call them.

| Method | File (owner) |
| --- | --- |
| `IServiceCollection AddApplication(this IServiceCollection)` | `src/Cadence.Application/DependencyInjection.cs` |
| `IServiceCollection AddPersistence(this IServiceCollection, string connectionString)` | `src/Cadence.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` |
| `IServiceCollection AddInfrastructure(this IServiceCollection, IConfiguration)` | `src/Cadence.Infrastructure/DependencyInjection.cs` |

`AddInfrastructure` calls `AddPersistence` internally and registers everything
else (parsers, cache, security, coaching). `Program.cs` calls only
`AddApplication()` and `AddInfrastructure(builder.Configuration)`.

## Configuration keys

Read through `IConfiguration`; every one has a Compose default.

| Key | Environment variable | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | Npgsql connection string |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | StackExchange.Redis configuration |
| `Jwt:Secret` | `Jwt__Secret` | HMAC signing key, >= 32 bytes |
| `Jwt:Issuer` / `Jwt:Audience` | `Jwt__Issuer` / `Jwt__Audience` | Token issuer/audience, default `cadence` |
| `Jwt:LifetimeMinutes` | `Jwt__LifetimeMinutes` | Default 720 |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | Optional. **Blank or absent means not configured** |
| `Anthropic:Model` | `Anthropic__Model` | Default `claude-opus-5` |
| `Storage:UploadDirectory` | `Storage__UploadDirectory` | Default `/data/uploads` |

An empty string must be treated as absent — Docker Compose substitutes unset
variables as `""`, and treating that as configured produces a service that
advertises a feature it cannot deliver.

## Ports (to avoid clashing with other projects on the same machine)

| Service | Host port |
| --- | --- |
| API | 8080 |
| Web | 5173 |
| PostgreSQL/PostGIS | 5434 |
| Redis | 6381 |

## HTTP conventions

- Route prefix `/api/v1`. Controllers, not minimal APIs.
- JSON: `camelCase` property naming, enums serialised as **strings**.
- Errors: RFC 7807 `ProblemDetails`. `ErrorKind` maps to status as
  `Validation → 400`, `NotFound → 404`, `Conflict → 409`, `Forbidden → 403`,
  `Unprocessable → 422`, `Unavailable → 503`.
- Auth: JWT bearer. The athlete id is the `sub` claim (a GUID).
- Every endpoint except `/api/v1/auth/*` and `/api/v1/health*` requires auth.

## Async and cancellation

Every I/O-bound method is `async` and takes a `CancellationToken` as its last
parameter, defaulted to `default`. Pass the token through; never swallow it.

## Style

- `sealed` on every class that is not designed for inheritance.
- Prefer `IReadOnlyList<T>` / `IReadOnlyCollection<T>` on public surfaces.
- Comments explain *why*, never *what*. No XML doc on private members.
- No `#pragma warning disable` to get past the build; fix the cause.
