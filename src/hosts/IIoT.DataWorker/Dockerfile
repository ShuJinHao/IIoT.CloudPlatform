FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.DataWorker/IIoT.DataWorker.csproj"
RUN dotnet publish "src/hosts/IIoT.DataWorker/IIoT.DataWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD dotnet IIoT.DataWorker.dll --healthcheck || exit 1

ENTRYPOINT ["dotnet", "IIoT.DataWorker.dll"]

