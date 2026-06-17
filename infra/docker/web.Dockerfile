# =============================================================================
# web.Dockerfile — Cerdik.Web (Blazor Web App)
# Multi-stage: SDK 10.0 (+ Node 20 for Tailwind) to build, aspnet:10.0 runtime.
# Build context: repository ROOT (see infra/docker/docker-compose.yml).
# Container listens on 8080 (host 5080 via compose).
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Install Node 20 + npm so the Tailwind CSS pipeline can run during publish.
# The MSBuild targets in Cerdik.Web (if present) invoke `npm` to compile CSS.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && node --version && npm --version \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Central build/version files first for cacheable restore.
COPY global.json ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy the full source tree.
COPY src/ ./src/

# If the web project ships a package.json, prime the npm cache before publish.
# (No-op when the file is absent; keeps the build robust either way.)
RUN if [ -f src/Cerdik.Web/package.json ]; then \
        npm --prefix src/Cerdik.Web ci || npm --prefix src/Cerdik.Web install; \
    fi

RUN dotnet restore src/Cerdik.Web/Cerdik.Web.csproj

RUN dotnet publish src/Cerdik.Web/Cerdik.Web.csproj \
    -c "$BUILD_CONFIGURATION" \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

# curl is used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Cerdik.Web.dll"]
