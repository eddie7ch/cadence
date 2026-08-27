# syntax=docker/dockerfile:1
#
# Build context is the repository root, not this directory - the restore needs
# Directory.Build.props and Directory.Packages.props, which live above it:
#
#   docker build -f src/Cadence.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Manifests first. This layer is invalidated only when a dependency or a project
# reference changes, so the restore below - the slow part - is reused across
# ordinary source edits. Copying the whole tree first would re-restore on every
# character typed in a .cs file.
COPY Directory.Build.props Directory.Packages.props Cadence.slnx ./
COPY src/Cadence.Domain/Cadence.Domain.csproj src/Cadence.Domain/
COPY src/Cadence.Application/Cadence.Application.csproj src/Cadence.Application/
COPY src/Cadence.Infrastructure/Cadence.Infrastructure.csproj src/Cadence.Infrastructure/
COPY src/Cadence.Api/Cadence.Api.csproj src/Cadence.Api/
COPY tests/Cadence.UnitTests/Cadence.UnitTests.csproj tests/Cadence.UnitTests/
COPY tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj tests/Cadence.IntegrationTests/

RUN dotnet restore src/Cadence.Api/Cadence.Api.csproj

COPY src/ src/

RUN dotnet publish src/Cadence.Api/Cadence.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.title="Cadence API" \
      org.opencontainers.image.description="Spatial and time-series athletic performance platform" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    Storage__UploadDirectory=/data/uploads

# curl is the only addition to the runtime image; it exists purely so the
# HEALTHCHECK below has something to make an HTTP request with.
RUN apt-get update \
 && apt-get install --yes --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# A fixed uid/gid rather than a generated one, so a bind-mounted upload
# directory has predictable ownership on the host. Creating and chowning
# /data/uploads here also matters for the named volume: Docker seeds an empty
# volume from the image's directory, ownership included, so the non-root
# process can write to it with no start-up chown and no root entrypoint.
RUN groupadd --system --gid 64198 cadence \
 && useradd --system --uid 64198 --gid 64198 --home-dir /app --shell /usr/sbin/nologin cadence \
 && mkdir -p /data/uploads \
 && chown -R cadence:cadence /app /data

COPY --from=build --chown=cadence:cadence /app/publish ./

USER cadence

EXPOSE 8080

# Liveness, not readiness: this answers "is the process serving HTTP", and must
# stay green while Postgres is briefly unavailable, or Compose would restart a
# perfectly healthy API every time the database blinked. Readiness, which does
# check the dependencies, is /api/v1/health/ready.
HEALTHCHECK --interval=10s --timeout=3s --start-period=25s --retries=6 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/api/v1/health || exit 1

ENTRYPOINT ["dotnet", "Cadence.Api.dll"]
