FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.Gateway/IIoT.Gateway.csproj"
RUN dotnet publish "src/hosts/IIoT.Gateway/IIoT.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl --fail http://localhost:8080/internal/healthz || exit 1

ENTRYPOINT ["dotnet", "IIoT.Gateway.dll"]
