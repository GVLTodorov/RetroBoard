# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from just the project files, so editing source later doesn't invalidate this layer.
COPY global.json Directory.Build.props ./
COPY src/RetroBoard.Infrastructure/RetroBoard.Infrastructure.csproj src/RetroBoard.Infrastructure/
COPY src/RetroBoard.Client/RetroBoard.Client.csproj src/RetroBoard.Client/
COPY src/RetroBoard.Api/RetroBoard.Api.csproj src/RetroBoard.Api/
RUN dotnet restore src/RetroBoard.Api/RetroBoard.Api.csproj

COPY src/RetroBoard.Infrastructure/ src/RetroBoard.Infrastructure/
COPY src/RetroBoard.Client/ src/RetroBoard.Client/
COPY src/RetroBoard.Api/ src/RetroBoard.Api/

# Publishing the Api project transitively publishes the Blazor Client's static assets into its
# output (the Api -> Client ProjectReference), so there is no separate Client publish step.
RUN dotnet publish src/RetroBoard.Api/RetroBoard.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed for the HEALTHCHECK below; the base image doesn't include it.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --uid 5678 --user-group --shell /usr/sbin/nologin appuser
COPY --from=build /app/publish .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "RetroBoard.Api.dll"]
