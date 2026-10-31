# Compendium — Storage Domain

Object-storage domain repo for the [Compendium](https://www.nuget.org/packages/Compendium.Core) event-sourcing framework, assembled per ADR-0007 (repo-per-domain topology). It hosts the storage abstraction and every storage adapter in a single repository, so a change to the `IObjectStore` port and its adapters ships as one atomic PR and one version train.

## Packages

| Package | Description |
|---------|-------------|
| `Compendium.Abstractions.Storage` | The `IObjectStore` port and its models: provider-agnostic object/blob storage (Put / Get / Delete / Exists / List / Presign). |
| `Compendium.Adapters.S3` | Adapter for S3-compatible object storage (AWS S3, Cloudflare R2, MinIO, Backblaze B2, Wasabi). Tenant-scoped keys, streaming uploads, multipart, SSE-S3/KMS, presigned URLs. |
| `Compendium.Adapters.Supabase` | Runtime adapter for Supabase Storage (cloud and self-hosted) over the native Storage REST API. Tenant-scoped keys, streaming uploads, presigned download URLs, signed upload URLs, public URLs, image transforms, per-connection storage factory. |
| `Compendium.Adapters.Supabase.Management` | Control-plane companion: stateless `ISupabaseAdmin` facade over the Supabase Cloud Management API (project CRUD, API keys) and the Storage admin API (buckets), plus a BYO / self-hosted attach probe. |
| `Compendium.Storage.Encryption` | Envelope encryption over any `IObjectStore`: `EncryptingObjectStore`, per-object AES-256-GCM data keys wrapped by a per-tenant key-encryption key (`IKeyEncryptionKeyProvider`), streamed in 64 KiB blocks with constant memory. |

All packages version together from git tags (MinVer, tag prefix `v`). The primary feed is GitHub Packages (`https://nuget.pkg.github.com/SCOJH/index.json`); releases are mirrored to nuget.org when a publish key is configured.

## Layout

```
src/
  Compendium.Abstractions.Storage/          # the port — adapters reference it via ProjectReference
  Compendium.Adapters.S3/
  Compendium.Adapters.Supabase/
  Compendium.Adapters.Supabase.Management/
  Compendium.Storage.Encryption/            # envelope-encryption decorator over IObjectStore
tests/
  Unit/                                     # one test project per source project, no I/O
  Integration/                              # Testcontainers-based, [RequiresDocker]-gated
```

The abstraction is consumed **in-repo** via `ProjectReference` — the atomic-PR benefit of the domain repo. Consumers outside this repo keep referencing the published `Compendium.Abstractions.Storage` package. Compendium base packages (`Compendium.Abstractions`, `Compendium.Core`, `Compendium.Multitenancy`) remain nuget.org `PackageReference` pins, managed centrally in `Directory.Packages.props`.

## Envelope encryption

`Compendium.Storage.Encryption` encrypts objects before they reach the underlying store. Each object gets a fresh
random AES-256 data key (DEK); the DEK is wrapped by the current tenant's key-encryption key (KEK) through the
`IKeyEncryptionKeyProvider` port, which the application implements (KMS, keys table wrapped by a master key, ...).
The ports (`IKeyEncryptionKeyProvider`, `IEncryptedObjectStore`) live in `Compendium.Abstractions.Storage.Encryption`.

```csharp
services.AddCompendiumS3(configuration);                       // the IObjectStore holding ciphertext
services.AddSingleton<IKeyEncryptionKeyProvider, MyKeyProvider>();
services.AddCompendiumStorageEncryption();                     // IEncryptedObjectStore

var put = await encrypted.PutAsync("files/f_123", upload);     // Result<EncryptedObject>
// Persist put.Value.StorageKey, put.Value.WrappedDek and put.Value.KekId with your own record.
var open = await encrypted.OpenReadAsync(storageKey, wrappedDek, kekId);
await using var file = open.Value;                             // plaintext stream, verified block by block
```

The wrapped DEK is returned to the caller and never stored in the object, so rotating a KEK re-wraps DEKs without
rewriting objects. Metadata passed to `PutAsync` is stored in clear by the underlying store.

Format, version 1:

- Header: `"CPSE"`, version byte `0x01`, KEK-identifier length (1 byte, 1 to 255), KEK identifier (UTF-8), 7-byte
  random nonce prefix. No key material.
- Blocks: 64 KiB of plaintext each (the last one 0 to 64 KiB, always present), AES-256-GCM, each followed by its
  16-byte tag. Nonce = prefix (7 bytes) + block index (4 bytes, big endian) + last-block flag (1 byte); every block
  authenticates the whole header as associated data.

Reads verify each block before returning any of its bytes. A wrong key, a foreign object or an altered header fail
`OpenReadAsync`; a modified, removed, reordered, duplicated or truncated later block, or appended data, makes the
read throw `CryptographicException`, which a consumer must treat as the whole object being invalid. Only the BCL
`AesGcm` is used; key buffers are wiped after use and nothing key-related is logged.

## Building

Every package targets `net9.0` and `net10.0` (both until .NET 9 leaves support on 2026-11-10). `global.json` asks for
the .NET 10 SDK, which builds both targets; to run the `net9.0` half of the tests you also need the .NET 9 runtime,
which the .NET 9 SDK brings. Consuming the packages from a `net9.0` project needs only the .NET 9 SDK.

```bash
dotnet build -c Release
dotnet test -c Release --filter "FullyQualifiedName!~IntegrationTests"   # unit tests, once per target
dotnet test -c Release                                                    # + integration (requires Docker)
```

CI enforces a 90% line-coverage gate on the unit-testable surface (currently ~96%).

## Releasing

Push a tag `v*` (e.g. `v1.1.0-preview.2`). The Release workflow builds, runs unit tests, packs all five packages, publishes to GitHub Packages, then to nuget.org when `NUGET_API_KEY` is available (soft-skips with a warning otherwise), and creates a GitHub Release.

## Provenance

Assembled from three source repositories — see [MIGRATION.md](MIGRATION.md) for the component → source-repo → SHA map.
