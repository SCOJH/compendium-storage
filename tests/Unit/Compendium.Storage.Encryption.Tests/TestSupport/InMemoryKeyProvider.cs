// -----------------------------------------------------------------------
// <copyright file="InMemoryKeyProvider.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Compendium.Abstractions.Storage.Encryption;

namespace Compendium.Storage.Encryption.Tests.TestSupport;

/// <summary>
/// Test <see cref="IKeyEncryptionKeyProvider"/>: one random in-memory KEK per tenant (rotatable), data keys
/// wrapped with AES-GCM bound to the tenant and KEK identifier. Keys are generated per test run, never fixed.
/// </summary>
internal sealed class InMemoryKeyProvider : IKeyEncryptionKeyProvider
{
    private readonly Dictionary<string, byte[]> _keks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);

    /// <summary>Gets the buffer last passed to <see cref="WrapAsync"/>, to check it is wiped after use.</summary>
    public ReadOnlyMemory<byte> LastWrappedInput { get; private set; }

    /// <summary>Gets the array last returned by <see cref="UnwrapAsync"/>, to check it is wiped after use.</summary>
    public byte[]? LastUnwrapped { get; private set; }

    /// <summary>Gets a copy of the last plaintext data key seen by <see cref="WrapAsync"/>.</summary>
    public byte[]? LastDataKeyCopy { get; private set; }

    public string CurrentKekId(string tenantId)
    {
        if (!_current.TryGetValue(tenantId, out var kekId))
        {
            kekId = Rotate(tenantId);
        }

        return kekId;
    }

    public string Rotate(string tenantId)
    {
        var kekId = $"kek-{tenantId}-{_keks.Count + 1}";
        _keks[kekId] = RandomNumberGenerator.GetBytes(32);
        _current[tenantId] = kekId;
        return kekId;
    }

    public Task<Result<WrappedDataKey>> WrapAsync(
        string tenantId,
        ReadOnlyMemory<byte> dataKey,
        CancellationToken cancellationToken = default)
    {
        LastWrappedInput = dataKey;
        LastDataKeyCopy = dataKey.ToArray();

        var kekId = CurrentKekId(tenantId);
        var wrapped = new byte[12 + dataKey.Length + 16];
        RandomNumberGenerator.Fill(wrapped.AsSpan(0, 12));
        using var aes = new AesGcm(_keks[kekId], 16);
        aes.Encrypt(
            wrapped.AsSpan(0, 12),
            dataKey.Span,
            wrapped.AsSpan(12, dataKey.Length),
            wrapped.AsSpan(12 + dataKey.Length),
            Binding(tenantId, kekId));

        return Task.FromResult(Result.Success(new WrappedDataKey(kekId, wrapped)));
    }

    public Task<Result<byte[]>> UnwrapAsync(
        string tenantId,
        string kekId,
        ReadOnlyMemory<byte> wrappedKey,
        CancellationToken cancellationToken = default)
    {
        if (!_keks.TryGetValue(kekId, out var kek) || wrappedKey.Length < 28)
        {
            return Task.FromResult<Result<byte[]>>(Error.NotFound("Test.UnknownKek", "Unknown KEK."));
        }

        var wrapped = wrappedKey.Span;
        var dataKey = new byte[wrapped.Length - 28];
        try
        {
            using var aes = new AesGcm(kek, 16);
            aes.Decrypt(wrapped[..12], wrapped[12..^16], wrapped[^16..], dataKey, Binding(tenantId, kekId));
        }
        catch (CryptographicException)
        {
            return Task.FromResult<Result<byte[]>>(Error.Forbidden("Test.UnwrapFailed", "Unwrap failed."));
        }

        LastUnwrapped = dataKey;
        return Task.FromResult(Result.Success(dataKey));
    }

    private static byte[] Binding(string tenantId, string kekId) => Encoding.UTF8.GetBytes($"{tenantId}\n{kekId}");
}
