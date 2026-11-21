# AeroFlow.ServiceDefaults (local package feed)

`lib/packages/` holds `AeroFlow.ServiceDefaults` and `AeroFlow.ServiceDefaults.Testing`
version **0.1.0**, packed from
[`template-dotnet-service`](https://github.com/aeroflow-air/template-dotnet-service)
at commit `50aac82d82c43b6274eb5bd1dda5fc397ac823fd` (PR #23). Conventions: [docs/service-defaults.md](../docs/service-defaults.md).

There is no org NuGet feed or `lib-service-defaults` repository yet, and the shared
`aeroflow-workflows` `dotnet-ci.yml` checkout does not fetch git submodules. A
committed local package feed is therefore the cleanest way to consume the library
in CI and Docker from this separate repo:

- `nuget.config` adds `lib/packages` as a source; the service and test projects
  use a `PackageReference` (the same shape as a future GitHub Packages reference).
- Restore and publish stay self-contained; no extra checkout is required.
- `aeroflow-local` references only this service's web project. An in-repo
  `ProjectReference` to vendored source was dropped during the AppHost restore,
  which then compiled the service against Microsoft.Extensions 8.x and failed
  CS1705 against a 10.x-built library. A `PackageReference` keeps the package
  graph intact for Aspire.

Do not hand-edit the `.nupkg` files. Re-pack from the template to update.

## Syncing from the template

```bash
./lib/sync-from-template.sh /path/to/template-dotnet-service
```

The script packs at the pinned commit and refreshes `docs/service-defaults.md`.
Update the SHA in this file if the pin changed.

## Swapping to GitHub Packages later

1. Publish `AeroFlow.ServiceDefaults` (and `.Testing`) to the org GitHub Packages feed.
2. In `nuget.config`, replace the `aeroflow-service-defaults` source with the
   feed URL (and add credentials as required by CI).
3. Delete `lib/packages/`, this README, and `lib/sync-from-template.sh`.
   Leave the `PackageReference`s as they are (update the version if needed).
