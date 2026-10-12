# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY AeroFlow.ServiceTemplate.sln ./
COPY src/AeroFlow.ServiceTemplate/AeroFlow.ServiceTemplate.csproj src/AeroFlow.ServiceTemplate/
COPY tests/AeroFlow.ServiceTemplate.Tests/AeroFlow.ServiceTemplate.Tests.csproj tests/AeroFlow.ServiceTemplate.Tests/

RUN dotnet restore AeroFlow.ServiceTemplate.sln

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/AeroFlow.ServiceTemplate/AeroFlow.ServiceTemplate.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "AeroFlow.ServiceTemplate.dll"]
