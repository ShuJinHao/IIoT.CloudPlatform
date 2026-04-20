FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/hosts/IIoT.HttpApi/IIoT.HttpApi.csproj"
RUN dotnet publish "src/hosts/IIoT.HttpApi/IIoT.HttpApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TZ=Asia/Shanghai

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IIoT.HttpApi.dll"]

