# Migration provenance

This repo was assembled on 2026-07-25 from three source repositories (ADR-0007, repo-per-domain topology). Sources were extracted read-only via `git archive` — no source repo was modified.

| Component | Source repo | Source ref (HEAD SHA) | Notes |
|-----------|-------------|-----------------------|-------|
| `src/Compendium.Abstractions.Storage/` | `sassy-solutions/compendium` (framework) | `origin/main` @ `792dd626496f69a4f7c86ce79f48795e6aba7e34` | Was `src/Abstractions/Compendium.Abstractions.Storage/`. The in-repo `ProjectReference` to `Compendium.Abstractions` became a nuget.org `PackageReference` (pin `1.0.5-preview.1`). `IsPackable=true` made explicit (the framework root props used to supply it). `PackageId` unchanged. |
| `tests/Unit/Compendium.Abstractions.Storage.Tests/` | `sassy-solutions/compendium` (framework) | same as above | Was `tests/Unit/Compendium.Abstractions.Storage.Tests/`. ProjectReference path re-rooted to `src/Compendium.Abstractions.Storage`. |
| `src/Compendium.Adapters.S3/` + `tests/{Unit,Integration}` | `sassy-solutions/compendium-adapter-s3` | `origin/main` @ `769b553cca835ab8e5aadf703450866968602322` | `Compendium.Abstractions.Storage` `PackageReference` replaced by `ProjectReference` to the in-repo abstraction. One-line API-drift fix: `S3ObjectStore.cs` L277 `Size: obj.Size ?? 0L` — the published `Compendium.Abstractions.Storage 1.0.3` binary the adapter previously compiled against accepted AWSSDK v4's nullable `S3Object.Size`, while the framework `origin/main` source declares `ObjectInfo.Size` as non-nullable `long`. `PackageId` unchanged. |
| `src/Compendium.Adapters.Supabase/` + `src/Compendium.Adapters.Supabase.Management/` + `tests/{Unit,Integration}` | `sassy-solutions/compendium-adapter-supabase` | `origin/main` @ `1f35e9329c6c044b0ccde9ffd72c37a8e272a2d6` | Both packages kept (runtime + Management). Runtime adapter's `Compendium.Abstractions.Storage` `PackageReference` replaced by `ProjectReference`; Management reaches the abstraction transitively via its existing `ProjectReference` to the runtime adapter. `PackageId`s unchanged. Note: the *local* clone's `main` was stale (`ff47bf8`, pre-Management) — `origin/main` was used. |
| Scaffold (`Directory.Build.props`, `global.json`, `.github/workflows/*`, `.gitignore`, `.config/dotnet-tools.json`, `LICENSE`) | `sassy-solutions/compendium-adapter-supabase` | same as above | Freshest release.yml (GitHub Packages first, nuget.org soft-skip). Feed URL re-pointed to `SCOJH`, nupkg assert raised to ≥ 4, repo/package URLs re-pointed to `SCOJH/storage`. reportgenerator tool pinned at 5.5.10 (highest of the two source repos). |
| `Directory.Packages.props` | union of both adapter repos | — | Version conflicts resolved by taking the highest (`MinVer 7.0.0`, `Microsoft.Extensions.* 10.0.8` where the S3 repo had bumped, `Microsoft.NET.Test.Sdk 18.5.1`, `coverlet.collector 10.0.0`). Compendium base packages pinned at `1.0.5-preview.1`. The `Compendium.Abstractions.Storage` pin was removed — it lives in this repo. |

## Not carried over

- `samples/` from both adapter repos (referenced the packages by nuget pin; re-add later against the domain packages if wanted).
- `docs/`, `CHANGELOG.md`, `.claude/` from the source repos — per-repo history stays in the source repos (to be archived in a later step).

## Versioning

This repo's version train starts at `v1.1.0-preview.1` — deliberately above the framework's `1.0.x` train so domain-repo packages win NuGet resolution over any previously published framework/adapter builds of the same IDs.
