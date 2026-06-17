# =============================================================================
# api.Dockerfile — Cerdik.Api (ASP.NET Core 10 Web API)
# Multi-stage: SDK 10.0 to build/publish, aspnet:10.0 runtime to run.
# Build context: repository ROOT (see infra/docker/docker-compose.yml).
# Container listens on 8080 (host 5081 via compose).
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy the central build/version files first so restore is cached independently
# of source churn. These live at the repo root.
COPY global.json ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy the source tree (the API project graph is referenced transitively).
COPY src/ ./src/

# Restore the API project graph (honours Central Package Management).
RUN dotnet restore src/Cerdik.Api/Cerdik.Api.csproj

# Publish only the API project. --no-restore reuses the layer above.
RUN dotnet publish src/Cerdik.Api/Cerdik.Api.csproj \
    -c "$BUILD_CONFIGURATION" \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Kestrel binds to all interfaces on 8080 (mapped to host 5081 by compose).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

# curl is used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Run as the non-root user shipped in the .NET runtime images (UID 64198).
USER $APP_UID

EXPOSE 8080

# Liveness probe — orchestrators (and `docker ps`) see container health.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Cerdik.Api.dll"]
