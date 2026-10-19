# HireZero in hosted mode (docs/adr/0026-container-and-releases.md). Base images are pinned by digest; update them
# as a reviewed change. The image serves members through OIDC behind an HTTPS reverse proxy; company data is in
# /data (SQLite) or in PostgreSQL through HIREZERO_DATABASE.

FROM node:22-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402 AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY config/ config/
COPY runtime/codex/pin.json runtime/codex/
COPY src/ src/
RUN dotnet restore src/HireZero.Host/HireZero.Host.csproj --locked-mode
COPY --from=web /src/src/HireZero.Host/wwwroot src/HireZero.Host/wwwroot
RUN dotnet publish src/HireZero.Host/HireZero.Host.csproj -c Release --no-restore -o /app \
    -p:ContinuousIntegrationBuild=true -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac
WORKDIR /app
COPY --from=build /app ./
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV HIREZERO_DATA_ROOT=/data \
    HIREZERO_LISTEN=http://0.0.0.0:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "HireZero.Host.dll", "--company-hosted"]
