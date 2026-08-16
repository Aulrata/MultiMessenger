# syntax=docker/dockerfile:1

# --- сборка ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только файлы проектов: слой с restore переиспользуется,
# пока не менялись зависимости, а меняются они реже исходников.
COPY Directory.Build.props Directory.Packages.props MultiMessenger.slnx ./
COPY src/MultiMessenger.Core/MultiMessenger.Core.csproj src/MultiMessenger.Core/
COPY src/MultiMessenger.Infrastructure/MultiMessenger.Infrastructure.csproj src/MultiMessenger.Infrastructure/
COPY src/MultiMessenger.Web/MultiMessenger.Web.csproj src/MultiMessenger.Web/

RUN dotnet restore src/MultiMessenger.Web/MultiMessenger.Web.csproj

COPY src/ src/

RUN dotnet publish src/MultiMessenger.Web/MultiMessenger.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# --- рантайм ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl нужен для HEALTHCHECK: в aspnet-образе его нет,
# а проверять живость контейнера чем-то надо.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Директория сессий Telegram/WhatsApp — на неё монтируется volume.
# Права выставляются до смены пользователя, иначе приложение не сможет туда писать.
RUN mkdir -p /data/sessions && chown -R $APP_UID:$APP_UID /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__SessionsBasePath=/data/sessions

EXPOSE 8080

# Процесс работает не от root: файлы сессий равносильны полному доступу
# к аккаунтам менеджеров, лишние права здесь ни к чему.
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MultiMessenger.Web.dll"]
