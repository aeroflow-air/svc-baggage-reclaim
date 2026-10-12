# Infrastructure

Bicep for this service will land here, using only governed platform modules (`br/platform:*`); see [ADR-0006](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0006-governed-bicep-modules-only.md).

Nothing is checked in yet on purpose — avoid inventing placeholder `.bicep` that drifts from platform standards. When the governed modules this service needs are ready, compose them under this folder and document the deploy path in the service README. Raw resources and Azure Verified Modules (AVM) are allowed only in sandboxes, not in this folder.
