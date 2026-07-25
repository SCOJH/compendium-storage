# Compendium — Storage Domain

Object-storage domain repo for the [Compendium](https://www.nuget.org/packages/Compendium.Core) event-sourcing framework, assembled per ADR-0007 (repo-per-domain topology). It hosts the storage abstraction and every storage adapter in a single repository, so a change to the `IObjectStore` port and its adapters ships as one atomic PR and one version train.

## Packages

| Package | Description |
|---------|-------------|
| `Compendium.Abstractions.Storage` | The `IObjectStore` port and its models: provider-agnostic object/blob storage (Put / Get / Delete / Exists / List / Presign). |
| `Compendium.Adapters.S3` | Adapter for S3-compatible object storage (AWS S3, Cloudflare R2, MinIO, Backblaze B2, Wasabi). Tenant-scoped keys, streaming uploads, multipart, SSE-S3/KMS, presigned URLs. |
| `Compendium.Adapters.Supabase` | Runtime adapter for Supabase Storage (cloud and self-hosted) over the native Storage REST API. Tenant-scoped keys, streaming uploads, presigned download URLs, signed upload URLs, public URLs, image transforms, per-connection storage factory. |
| `Compendium.Adapters.Supabase.Management` | Control-plane companion: stateless `ISupabaseAdmin` facade over the Supabase Cloud Management API (project CRUD, API keys) and the Storage admin API (buckets), plus a BYO / self-hosted attach probe. |

All packages version together from git tags (MinVer, tag prefix `v`). The primary feed is GitHub Packages (`https://nuget.pkg.github.com/SCOJH/index.json`); releases are mirrored to nuget.org when a publish key is configured.

## Layout

```
src/
  Compendium.Abstractions.Storage/          # the port — adapters reference it via ProjectReference
  Compendium.Adapters.S3/
  Compendium.Adapters.Supabase/
  Compendium.Adapters.Supabase.Management/
tests/
  Unit/                                     # one test project per source project, no I/O
  Integration/                              # Testcontainers-based, [RequiresDocker]-gated
```

The abstraction is consumed **in-repo** via `ProjectReference` — the atomic-PR benefit of the domain repo. Consumers outside this repo keep referencing the published `Compendium.Abstractions.Storage` package. Compendium base packages (`Compendium.Abstractions`, `Compendium.Core`, `Compendium.Multitenancy`) remain nuget.org `PackageReference` pins, managed centrally in `Directory.Packages.props`.

## Building

```bash
dotnet build -c Release
dotnet test -c Release --filter "FullyQualifiedName!~IntegrationTests"   # unit tests
dotnet test -c Release                                                    # + integration (requires Docker)
```

CI enforces a 90% line-coverage gate on the unit-testable surface (currently ~96%).

## Releasing

Push a tag `v*` (e.g. `v1.1.0-preview.2`). The Release workflow builds, runs unit tests, packs all four packages, publishes to GitHub Packages, then to nuget.org when `NUGET_API_KEY` is available (soft-skips with a warning otherwise), and creates a GitHub Release.

## Provenance

Assembled from three source repositories — see [MIGRATION.md](MIGRATION.md) for the component → source-repo → SHA map.
