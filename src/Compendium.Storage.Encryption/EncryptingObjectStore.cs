// -----------------------------------------------------------------------
// <copyright file="EncryptingObjectStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Abstractions.Storage.Models;
using Compendium.Multitenancy;
using Compendium.Storage.Encryption.Format;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Storage.Encryption;

/// <summary>
/// Envelope encryption over any <see cref="IObjectStore"/>: each object is encrypted with its own random
/// AES-256 data key, streamed in 64 KiB AES-GCM blocks, and the data key is wrapped by the current tenant's
/// key-encryption key through <see cref="IKeyEncryptionKeyProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The stored object is a header (format version, KEK identifier, nonce prefix) followed by the sealed
/// blocks; it never contains the data key. <see cref="PutAsync"/> hands the wrapped data key back to the
/// caller, which keeps it and passes it to <see cref="OpenReadAsync"/>. A KEK rotation therefore re-wraps data
/// keys without rewriting objects.
/// </para>
/// <para>
/// Writes and reads stream with constant memory. Reads verify each block before returning any of its bytes and
/// detect modified, removed, reordered and truncated blocks as well as appended data.
/// </para>
/// </remarks>
public sealed class EncryptingObjectStore : IEncryptedObjectStore
{
    private readonly IObjectStore _inner;
    private readonly IKeyEncryptionKeyProvider _keys;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<EncryptingObjectStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptingObjectStore"/> class.
    /// </summary>
    /// <param name="inner">The store that holds the encrypted objects.</param>
    /// <param name="keys">The provider of the tenants' key-encryption keys.</param>
    /// <param name="tenantAccessor">The accessor of the tenant in scope, whose key is used.</param>
    /// <param name="logger">Optional logger. Only storage keys and error codes are logged, never key material.</param>
    public EncryptingObjectStore(
        IObjectStore inner,
        IKeyEncryptionKeyProvider keys,
        ITenantContextAccessor tenantAccessor,
        ILogger<EncryptingObjectStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(tenantAccessor);

        _inner = inner;
        _keys = keys;
        _tenantAccessor = tenantAccessor;
        _logger = logger ?? NullLogger<EncryptingObjectStore>.Instance;
    }

    /// <inheritdoc />
    public async Task<Result<EncryptedObject>> PutAsync(
        string key,
        Stream content,
        ObjectMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId))
        {
            return StorageEncryptionErrors.NoTenant();
        }

        var dataKey = new byte[EnvelopeFormat.DataKeySize];
        WrappedDataKey wrapped;
        AesGcm aes;
        try
        {
            RandomNumberGenerator.Fill(dataKey);

            var wrapResult = await _keys.WrapAsync(tenantId, dataKey, cancellationToken).ConfigureAwait(false);
            if (wrapResult.IsFailure)
            {
                return wrapResult.Error;
            }

            wrapped = wrapResult.Value!;
            if (!EncryptedObjectHeader.IsValidKekId(wrapped.KekId))
            {
                return StorageEncryptionErrors.InvalidKekId();
            }

            aes = new AesGcm(dataKey, EnvelopeFormat.TagSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }

        await using var encrypting = new EncryptingReadStream(content, aes, EncryptedObjectHeader.Create(wrapped.KekId));

        var put = await _inner.PutAsync(key, encrypting, metadata, cancellationToken).ConfigureAwait(false);
        if (put.IsFailure)
        {
            return put.Error;
        }

        if (!encrypting.Completed)
        {
            // A partial object would only fail later, at read time: refuse it now.
            _logger.LogWarning("Store did not consume the whole encrypted content of {Key} ; deleting it", key);
            await _inner.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            return StorageEncryptionErrors.IncompleteWrite(key);
        }

        return new EncryptedObject(key, wrapped.WrappedKey, wrapped.KekId, encrypting.PlaintextLength, put.Value!);
    }

    /// <inheritdoc />
    public async Task<Result<ObjectStream>> OpenReadAsync(
        string storageKey,
        ReadOnlyMemory<byte> wrappedDek,
        string kekId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId))
        {
            return StorageEncryptionErrors.NoTenant();
        }

        if (!EncryptedObjectHeader.IsValidKekId(kekId))
        {
            return StorageEncryptionErrors.InvalidKekId();
        }

        var get = await _inner.GetAsync(storageKey, cancellationToken).ConfigureAwait(false);
        if (get.IsFailure)
        {
            return get.Error;
        }

        var stored = get.Value!;
        Result<(EncryptedObjectHeader Header, AesGcm Aes)> opened;
        try
        {
            opened = await OpenAsync(stored, storageKey, tenantId, kekId, wrappedDek, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await stored.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (opened.IsFailure)
        {
            await stored.DisposeAsync().ConfigureAwait(false);
            return opened.Error;
        }

        var (header, aes) = opened.Value;
        var decrypting = new DecryptingReadStream(stored.Content, aes, header);
        try
        {
            await decrypting.PrefetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Encrypted object {Key} failed integrity verification", storageKey);
            await decrypting.DisposeAsync().ConfigureAwait(false);
            return StorageEncryptionErrors.IntegrityCheckFailed(storageKey);
        }
        catch
        {
            await decrypting.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var info = stored.Info with { Size = EnvelopeFormat.PlaintextLength(stored.Info.Size, header.Bytes.Length) };
        return new ObjectStream(decrypting, info);
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(storageKey, cancellationToken);

    private async Task<Result<(EncryptedObjectHeader Header, AesGcm Aes)>> OpenAsync(
        ObjectStream stored,
        string storageKey,
        string tenantId,
        string kekId,
        ReadOnlyMemory<byte> wrappedDek,
        CancellationToken cancellationToken)
    {
        var header = await EncryptedObjectHeader.ReadAsync(stored.Content, storageKey, cancellationToken)
            .ConfigureAwait(false);
        if (header.IsFailure)
        {
            _logger.LogWarning("Encrypted object {Key} rejected: {Code}", storageKey, header.Error.Code);
            return header.Error;
        }

        var unwrap = await _keys.UnwrapAsync(tenantId, kekId, wrappedDek, cancellationToken).ConfigureAwait(false);
        if (unwrap.IsFailure)
        {
            return unwrap.Error;
        }

        var dataKey = unwrap.Value!;
        try
        {
            if (dataKey.Length != EnvelopeFormat.DataKeySize)
            {
                return StorageEncryptionErrors.InvalidDataKey();
            }

            return (header.Value!, new AesGcm(dataKey, EnvelopeFormat.TagSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private bool TryResolveTenant(out string tenantId)
    {
        var ctx = _tenantAccessor.TenantContext;
        if (ctx is null || !ctx.HasTenant || string.IsNullOrWhiteSpace(ctx.TenantId))
        {
            tenantId = string.Empty;
            return false;
        }

        tenantId = ctx.TenantId;
        return true;
    }
}
