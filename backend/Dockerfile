FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MestoBeach.sln .
COPY src/MestoBeach.API/MestoBeach.API.csproj src/MestoBeach.API/
COPY src/Shared/MestoBeach.Shared/MestoBeach.Shared.csproj src/Shared/MestoBeach.Shared/
COPY src/Modules/Identity/MestoBeach.Identity.Domain/MestoBeach.Identity.Domain.csproj src/Modules/Identity/MestoBeach.Identity.Domain/
COPY src/Modules/Identity/MestoBeach.Identity.Application/MestoBeach.Identity.Application.csproj src/Modules/Identity/MestoBeach.Identity.Application/
COPY src/Modules/Identity/MestoBeach.Identity.Infrastructure/MestoBeach.Identity.Infrastructure.csproj src/Modules/Identity/MestoBeach.Identity.Infrastructure/
COPY src/Modules/Catalog/MestoBeach.Catalog.Domain/MestoBeach.Catalog.Domain.csproj src/Modules/Catalog/MestoBeach.Catalog.Domain/
COPY src/Modules/Catalog/MestoBeach.Catalog.Application/MestoBeach.Catalog.Application.csproj src/Modules/Catalog/MestoBeach.Catalog.Application/
COPY src/Modules/Catalog/MestoBeach.Catalog.Infrastructure/MestoBeach.Catalog.Infrastructure.csproj src/Modules/Catalog/MestoBeach.Catalog.Infrastructure/
COPY src/Modules/Sessions/MestoBeach.Sessions.Domain/MestoBeach.Sessions.Domain.csproj src/Modules/Sessions/MestoBeach.Sessions.Domain/
COPY src/Modules/Sessions/MestoBeach.Sessions.Application/MestoBeach.Sessions.Application.csproj src/Modules/Sessions/MestoBeach.Sessions.Application/
COPY src/Modules/Sessions/MestoBeach.Sessions.Infrastructure/MestoBeach.Sessions.Infrastructure.csproj src/Modules/Sessions/MestoBeach.Sessions.Infrastructure/
COPY src/Modules/Orders/MestoBeach.Orders.Domain/MestoBeach.Orders.Domain.csproj src/Modules/Orders/MestoBeach.Orders.Domain/
COPY src/Modules/Orders/MestoBeach.Orders.Application/MestoBeach.Orders.Application.csproj src/Modules/Orders/MestoBeach.Orders.Application/
COPY src/Modules/Orders/MestoBeach.Orders.Infrastructure/MestoBeach.Orders.Infrastructure.csproj src/Modules/Orders/MestoBeach.Orders.Infrastructure/
COPY src/Modules/Payments/MestoBeach.Payments.Domain/MestoBeach.Payments.Domain.csproj src/Modules/Payments/MestoBeach.Payments.Domain/
COPY src/Modules/Payments/MestoBeach.Payments.Application/MestoBeach.Payments.Application.csproj src/Modules/Payments/MestoBeach.Payments.Application/
COPY src/Modules/Payments/MestoBeach.Payments.Infrastructure/MestoBeach.Payments.Infrastructure.csproj src/Modules/Payments/MestoBeach.Payments.Infrastructure/
COPY src/Modules/Analytics/MestoBeach.Analytics.Domain/MestoBeach.Analytics.Domain.csproj src/Modules/Analytics/MestoBeach.Analytics.Domain/
COPY src/Modules/Analytics/MestoBeach.Analytics.Application/MestoBeach.Analytics.Application.csproj src/Modules/Analytics/MestoBeach.Analytics.Application/
COPY src/Modules/Analytics/MestoBeach.Analytics.Infrastructure/MestoBeach.Analytics.Infrastructure.csproj src/Modules/Analytics/MestoBeach.Analytics.Infrastructure/
COPY src/Modules/Notifications/MestoBeach.Notifications.Domain/MestoBeach.Notifications.Domain.csproj src/Modules/Notifications/MestoBeach.Notifications.Domain/
COPY src/Modules/Notifications/MestoBeach.Notifications.Application/MestoBeach.Notifications.Application.csproj src/Modules/Notifications/MestoBeach.Notifications.Application/
COPY src/Modules/Notifications/MestoBeach.Notifications.Infrastructure/MestoBeach.Notifications.Infrastructure.csproj src/Modules/Notifications/MestoBeach.Notifications.Infrastructure/

RUN dotnet restore src/MestoBeach.API/MestoBeach.API.csproj

COPY src/ src/
RUN dotnet publish src/MestoBeach.API/MestoBeach.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5011
EXPOSE 5011

ENTRYPOINT ["dotnet", "MestoBeach.API.dll"]

# ─── Inicializador del entorno de demostración (perfil "demo") ────────────────
# OJO: esta es la ÚLTIMA etapa del fichero, así que es la que Docker construye
# cuando no se le pasa `--target`. Por eso el servicio `api` de docker-compose.yml
# declara `target: runtime` de forma explícita. Si añades etapas debajo, respétalo.
# Parte de la capa `build`, que ya tiene el SDK, el código y los paquetes
# restaurados: reutilizarla hace que esta imagen no ocupe disco extra apreciable.
# Añade dotnet-ef (migraciones), psql (bootstrap) y curl/jq (seed por API).
FROM build AS initializer
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl jq postgresql-client \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet tool install --global dotnet-ef --version 8.0.11
ENV PATH="/root/.dotnet/tools:${PATH}"
WORKDIR /src
ENTRYPOINT ["/scripts/docker-init.sh"]
