// -----------------------------------------------------------------------
// <copyright file="WrappedDataKey.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Storage.Encryption;

/// <summary>
/// A data key wrapped by a key-encryption key, returned by <see cref="IKeyEncryptionKeyProvider.WrapAsync"/>.
/// </summary>
/// <param name="KekId">The identifier of the key-encryption key that wrapped the data key.</param>
/// <param name="WrappedKey">The wrapped data key. Opaque to the storage layer.</param>
public sealed record WrappedDataKey(string KekId, ReadOnlyMemory<byte> WrappedKey);
