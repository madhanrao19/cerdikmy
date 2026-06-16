# =============================================================================
# worker.Dockerfile — Cerdik.Worker (Hangfire background worker host)
# Multi-stage: SDK 10.0 to build, aspnet:10.0 runtime to run.
# No public port — this host only processes Hangfire jobs.
# Build context: repository ROOT (see infra/docker/docker-compose.yml).
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY global.json ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY Cerdik.sln ./

COPY src/ ./src/

RUN dotnet restore Cerdik.sln

RUN dotnet publish src/Cerdik.Worker/Cerdik.Worker.csproj \
    -c "$BUILD_CONFIGURATION" \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

# ---- Runtime stage ----------------------------------------------------------
# aspnet runtime carries the ASP.NET shared framework that Hangfire.AspNetCore
# and the generic host depend on, even though no port is exposed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

COPY --from=build /app/publish ./

USER $APP_UID

# No EXPOSE — worker has no inbound traffic.

ENTRYPOINT ["dotnet", "Cerdik.Worker.dll"]
