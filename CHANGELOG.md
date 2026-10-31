# Changelog

All notable changes to the Compendium Storage packages (`Compendium.Abstractions.Storage`, `Compendium.Adapters.S3`,
`Compendium.Adapters.Supabase`, `Compendium.Adapters.Supabase.Management`, `Compendium.Storage.Encryption`) are
recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow the `v*` tags. Earlier versions are
described in the generated notes of the [GitHub Releases](https://github.com/SCOJH/compendium-storage/releases).

## [Unreleased]

### Added

- **`Compendium.Storage.Encryption`: envelope encryption over any `IObjectStore`.** `EncryptingObjectStore`
  encrypts each object with its own random AES-256 data key, streamed as AES-256-GCM blocks of 64 KiB behind a
  versioned header (format version, KEK identifier, nonce prefix); the nonce is prefix + block counter + last-block
  flag, so a modified, removed, reordered or truncated block, or appended data, is refused. Reads and writes stream
  with constant memory, and no byte of a block is returned before it verified. The data key is wrapped per tenant by
  the new `IKeyEncryptionKeyProvider` port and handed back to the caller (`PutAsync` returns `StorageKey`,
  `WrappedDek`, `KekId`; `OpenReadAsync` takes them), never stored in the object, so a KEK rotation re-wraps data
  keys without rewriting objects. The ports (`IKeyEncryptionKeyProvider`, `IEncryptedObjectStore`, `WrappedDataKey`,
  `EncryptedObject`) are added to `Compendium.Abstractions.Storage` under the `Encryption` namespace; registration
  with `AddCompendiumStorageEncryption()`. (FIL-04, agent-spot/backlog#228)

### Changed

- **Every package now targets `net9.0` and `net10.0`.** .NET 9 (STS) leaves support on 2026-11-10; .NET 10 is the
  LTS consumers are moving to, and `net9.0` stays for the transition only. `global.json` moves to the .NET 10 SDK
  (10.0.100 or any later 10.0 feature band), which builds both targets; the CI and release workflows install the
  .NET 9 SDK beside it for its runtime, so every unit test runs once per target. On `net10.0` the
  `Microsoft.Extensions.*` family resolves to 10.0.x, which packages built for .NET 10 require; `net9.0` keeps the
  9.0.16 line it already shipped with, so a `net9.0` consumer's dependency graph is unchanged. The packed nuspecs
  carry one dependency group per target. The Compendium base packages (`Compendium.Abstractions`,
  `Compendium.Core`, `Compendium.Multitenancy`) stay at 1.0.5-preview.1: their `net9.0` assets are what the
  `net10.0` target consumes. (NET10-01, agent-spot/backlog#363)

[Unreleased]: https://github.com/SCOJH/compendium-storage/compare/v1.1.0-preview.3...HEAD
