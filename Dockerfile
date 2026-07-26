# syntax=docker/dockerfile:1

# ---------------------------------------------------------------- build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Once sadece proje dosyalari kopyalanir: bagimlilik katmani, kaynak kod
# degistiginde yeniden cozulmez.
COPY NuGet.config ./
COPY src/DokployMonitor.Core/DokployMonitor.Core.csproj src/DokployMonitor.Core/
COPY src/DokployMonitor.Infrastructure/DokployMonitor.Infrastructure.csproj src/DokployMonitor.Infrastructure/
COPY src/DokployMonitor.Web/DokployMonitor.Web.csproj src/DokployMonitor.Web/
RUN dotnet restore src/DokployMonitor.Web/DokployMonitor.Web.csproj

COPY src/ src/
RUN dotnet publish src/DokployMonitor.Web/DokployMonitor.Web.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# --------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logs__MountPath=/app/dokploy-logs \
    Logs__ArchivePath=/app/data/log-archive \
    DataProtection__KeysPath=/app/data/dataprotection-keys

# ConnectionStrings__Default zorunlu: Dokploy Environment'tan SQL Server baglanti dizesi verin.
# Ornek: Server=mssql;Database=DokployMonitor;User Id=sa;Password=...;TrustServerCertificate=True

COPY --from=build /app/publish ./

# Log arsivi + DataProtection anahtarlari icin yazilabilir dizin (root olmayan kullanici).
RUN mkdir -p /app/data/dataprotection-keys /app/dokploy-logs && chown -R $APP_UID:$APP_UID /app/data

USER $APP_UID
EXPOSE 8080

# Saglik ucu: /health (Dokploy veya Traefik buradan kontrol edebilir).
ENTRYPOINT ["dotnet", "DokployMonitor.Web.dll"]
