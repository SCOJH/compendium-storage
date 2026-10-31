// -----------------------------------------------------------------------
// <copyright file="IKeyEncryptionKeyProvider.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Storage.Encryption;

/// <summary>
/// Wraps and unwraps per-object data keys (DEK) with a tenant's key-encryption key (KEK).
/// </summary>
/// <remarks>
/// <para>
/// Envelope encryption: every object is encrypted with its own random data key, and only the
/// data key is encrypted with the tenant's key-encryption key. Implementations hold the KEKs
/// (a key-management service, a database of keys wrapped by a platform master key, ...); the
/// storage layer never sees them.
/// </para>
/// <para>
/// Implementations must never log key material, and must fail (rather than return arbitrary bytes)
/// when a wrapped key was not produced by the identified KEK for that tenant.
/// </para>
/// </remarks>
public interface IKeyEncryptionKeyProvider
{
    /// <summary>
    /// Wraps a data key with the tenant's current key-encryption key.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the data.</param>
    /// <param name="dataKey">The plaintext data key to wrap. The provider must not retain it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wrapped data key and the identifier of the KEK that wrapped it, or an error.</returns>
    Task<Result<WrappedDataKey>> WrapAsync(
        string tenantId,
        ReadOnlyMemory<byte> dataKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unwraps a data key previously wrapped for the tenant by the identified key-encryption key.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the data.</param>
    /// <param name="kekId">The identifier of the KEK that wrapped <paramref name="wrappedKey"/>.</param>
    /// <param name="wrappedKey">The wrapped data key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The plaintext data key, or an error. Ownership of the returned array passes to the caller,
    /// which zeroes it once it no longer needs it.
    /// </returns>
    Task<Result<byte[]>> UnwrapAsync(
        string tenantId,
        string kekId,
        ReadOnlyMemory<byte> wrappedKey,
        CancellationToken cancellationToken = default);
}
