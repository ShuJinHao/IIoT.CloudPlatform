ARG DOTNET_SDK_IMAGE=harbor.internal.example/mirror/dotnet-sdk:10.0.301
ARG DOTNET_ASPNET_IMAGE=harbor.internal.example/mirror/dotnet-aspnet:10.0.9

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.MigrationWorkApp/IIoT.MigrationWorkApp.csproj"
RUN dotnet publish "src/hosts/IIoT.MigrationWorkApp/IIoT.MigrationWorkApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ${DOTNET_ASPNET_IMAGE} AS final
WORKDIR /app
ARG APP_UID=10001
ARG APP_GID=10001

RUN groupadd --gid "${APP_GID}" iiot \
    && useradd --uid "${APP_UID}" --gid "${APP_GID}" --create-home --shell /usr/sbin/nologin iiot \
    && mkdir -p /app/logs \
    && chown -R "${APP_UID}:${APP_GID}" /app

ENV DOTNET_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

COPY --from=build --chown=${APP_UID}:${APP_GID} /app/publish .
USER ${APP_UID}:${APP_GID}

ENTRYPOINT ["dotnet", "IIoT.MigrationWorkApp.dll"]
