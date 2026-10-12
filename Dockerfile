# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY AeroFlow.BaggageReclaim.sln ./
COPY src/AeroFlow.BaggageReclaim/AeroFlow.BaggageReclaim.csproj src/AeroFlow.BaggageReclaim/
COPY tests/AeroFlow.BaggageReclaim.Tests/AeroFlow.BaggageReclaim.Tests.csproj tests/AeroFlow.BaggageReclaim.Tests/

RUN dotnet restore AeroFlow.BaggageReclaim.sln

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/AeroFlow.BaggageReclaim/AeroFlow.BaggageReclaim.csproj \
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
ENTRYPOINT ["dotnet", "AeroFlow.BaggageReclaim.dll"]
