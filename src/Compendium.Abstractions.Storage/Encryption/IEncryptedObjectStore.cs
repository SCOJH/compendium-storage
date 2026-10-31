// -----------------------------------------------------------------------
// <copyright file="IEncryptedObjectStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage.Models;

namespace Compendium.Abstractions.Storage.Encryption;

/// <summary>
/// Stores objects encrypted with a per-object data key, wrapped by the current tenant's key-encryption key
/// (envelope encryption) on top of an <see cref="IObjectStore"/>.
/// </summary>
/// <remarks>
/// The wrapped data key is handed back to the caller, which stores it with its own record of the object and
/// passes it back to read the object. Reads stream: content is decrypted and verified block by block, so
/// memory use does not grow with the size of the object.
/// </remarks>
public interface IEncryptedObjectStore
{
    /// <summary>
    /// Encrypts <paramref name="content"/> under a fresh data key and uploads it to <paramref name="key"/>,
    /// replacing any existing object at the same key.
    /// </summary>
    /// <param name="key">The object key (path) inside the bucket / container.</param>
    /// <param name="content">The readable plaintext stream to encrypt and upload. It is read to its end.</param>
    /// <param name="metadata">
    /// Optional metadata, stored <b>in clear</b> by the underlying store: do not put confidential values in it.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <see cref="EncryptedObject"/> carrying the wrapped data key to persist, or an error.</returns>
    Task<Result<EncryptedObject>> PutAsync(
        string key,
        Stream content,
        ObjectMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an encrypted object for streamed, verified reading.
    /// </summary>
    /// <remarks>
    /// The header and the first block are verified before this method returns, so a wrong key, a foreign object
    /// or an altered header surface as an error result. Later blocks are verified as they are read: a
    /// modified, removed, reordered or truncated block makes the read throw
    /// <see cref="System.Security.Cryptography.CryptographicException"/>, and no unverified byte is ever returned.
    /// A consumer must treat such an exception as the whole object being invalid.
    /// The returned <see cref="ObjectInfo.Size"/> is the plaintext size, derived from the stored size.
    /// </remarks>
    /// <param name="storageKey">The <see cref="EncryptedObject.StorageKey"/> returned by <see cref="PutAsync"/>.</param>
    /// <param name="wrappedDek">The wrapped data key returned by <see cref="PutAsync"/> (or re-wrapped since).</param>
    /// <param name="kekId">The identifier of the key-encryption key that wrapped <paramref name="wrappedDek"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="ObjectStream"/> over the plaintext, which the caller disposes; or an error.</returns>
    Task<Result<ObjectStream>> OpenReadAsync(
        string storageKey,
        ReadOnlyMemory<byte> wrappedDek,
        string kekId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an encrypted object. Deleting a non-existent key returns success.
    /// </summary>
    /// <param name="storageKey">The storage key of the object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or an error.</returns>
    Task<Result> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
