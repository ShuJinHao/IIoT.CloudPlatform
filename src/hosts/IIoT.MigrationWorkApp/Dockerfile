ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.301
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.9

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.MigrationWorkApp/IIoT.MigrationWorkApp.csproj"
RUN dotnet publish "src/hosts/IIoT.MigrationWorkApp/IIoT.MigrationWorkApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ${DOTNET_ASPNET_IMAGE} AS final
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IIoT.MigrationWorkApp.dll"]
