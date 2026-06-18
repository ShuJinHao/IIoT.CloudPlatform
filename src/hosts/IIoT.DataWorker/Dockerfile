ARG DOTNET_SDK_IMAGE=10.98.90.154:80/mirror/dotnet-sdk:10.0.301
ARG DOTNET_ASPNET_IMAGE=10.98.90.154:80/mirror/dotnet-aspnet:10.0.9

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.DataWorker/IIoT.DataWorker.csproj"
RUN dotnet publish "src/hosts/IIoT.DataWorker/IIoT.DataWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ${DOTNET_ASPNET_IMAGE} AS final
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD dotnet IIoT.DataWorker.dll --healthcheck || exit 1

ENTRYPOINT ["dotnet", "IIoT.DataWorker.dll"]
