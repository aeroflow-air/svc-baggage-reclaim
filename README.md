# AeroFlow .NET service template

Golden-path ASP.NET Core Web API template for the [AeroFlow Air](https://github.com/aeroflow-air) portfolio. Squads of roughly six use this to spin up `svc-*` repositories with a thin, containerised starting point — health checks, structured logging, ProblemDetails, and a stub for OpenTelemetry — without a shared framework package.

## Create a new service from this template

**Preferred:** on GitHub, open this repository and choose **Use this template** → create `svc-booking` (or similar) under `aeroflow-air`.

**Or clone and rename:**

```bash
git clone https://github.com/aeroflow-air/template-dotnet-service.git svc-booking
cd svc-booking
# Rename solution, project folders, namespaces, and Docker image tags to match the service
```

Keep the layout: `src/`, `tests/`, `Dockerfile`, and `.github/workflows/ci.yml`. Rename `AeroFlow.ServiceTemplate` to your service name throughout.

## Local run

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore
dotnet run --project src/AeroFlow.ServiceTemplate
```

- API: `http://localhost:8080` (or the port shown in the console)
- Health: `GET /health`
- Demo booking probe: `GET /api/bookings/ping` and `GET /api/bookings/{id}` (unknown ids return ProblemDetails)

```bash
dotnet test
```

## Container

```bash
docker build -t aeroflow-service-template .
docker run --rm -p 8080:8080 aeroflow-service-template
```

Then `curl http://localhost:8080/health`.

## What is deliberately not included

- **No Kubernetes** manifests or Helm charts
- **No Dagger** pipelines
- **No Pulumi** (or other IaC frameworks in-repo yet)
- **No heavy shared framework** NuGet — composition stays in `Program.cs` so squads can delete or replace pieces freely

Infrastructure as Bicep will land under [`infra/`](infra/README.md) later, using only governed platform modules (`br/platform:*`); see [ADR-0006](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0006-governed-bicep-modules-only.md). Platform conventions live in the **platform-handbook**; reusable Actions come from **aeroflow-workflows**.

## CI

`.github/workflows/ci.yml` calls the reusable workflow in `aeroflow-workflows` (`dotnet-ci.yml`, pinned to `@v0.1.0`), which restores, builds, and tests on pushes to `main` and on pull requests. Job-level `permissions` are explicit because the organisation `GITHUB_TOKEN` is read-only by default.

## Pointers

| Resource | Purpose |
| --- | --- |
| platform-handbook | Portfolio standards, CLAUDE.md constraints, ADR process |
| aeroflow-workflows | Shared GitHub Actions (dotnet-ci and decisions validation) |
| `infra/` | Placeholder for Bicep (governed `br/platform:*` modules only, see [ADR-0006](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0006-governed-bicep-modules-only.md)) — see `infra/README.md` |

## Licence / ownership

Internal AeroFlow Air template. Public repository; treat as the default starting point for new .NET services unless an ADR says otherwise.
