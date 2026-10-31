// -----------------------------------------------------------------------
// <copyright file="EncryptedObject.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage.Models;

namespace Compendium.Abstractions.Storage.Encryption;

/// <summary>
/// Describes an object written by <see cref="IEncryptedObjectStore.PutAsync"/>.
/// </summary>
/// <remarks>
/// The caller must persist <see cref="WrappedDek"/> and <see cref="KekId"/> next to its own record of the
/// object: they are not stored in the object, and without them the object cannot be read. Keeping them with
/// the caller is what lets a KEK rotation re-wrap the data keys without rewriting the objects.
/// </remarks>
/// <param name="StorageKey">The key to pass back to <see cref="IEncryptedObjectStore.OpenReadAsync"/>.</param>
/// <param name="WrappedDek">The object's data key, wrapped by the tenant's key-encryption key.</param>
/// <param name="KekId">The identifier of the key-encryption key that wrapped <see cref="WrappedDek"/>.</param>
/// <param name="PlaintextLength">The number of plaintext bytes that were encrypted.</param>
/// <param name="Info">The stored object as described by the underlying store (sizes are ciphertext sizes).</param>
public sealed record EncryptedObject(
    string StorageKey,
    ReadOnlyMemory<byte> WrappedDek,
    string KekId,
    long PlaintextLength,
    ObjectInfo Info);
