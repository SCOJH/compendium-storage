// -----------------------------------------------------------------------
// <copyright file="EncryptedObjectHeader.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace Compendium.Storage.Encryption.Format;

/// <summary>
/// The header of an encrypted object: <c>"CPSE" | version (1 byte) | kekId length (1 byte) | kekId (UTF-8) |
/// nonce prefix (7 bytes)</c>.
/// </summary>
/// <remarks>
/// The header carries no key material: the wrapped data key is kept by the caller. The KEK identifier is the
/// one in use when the object was written; it is not read back, since after a rotation the caller's copy names
/// a newer KEK, which is expected. The header is authenticated by every block, so any change to it, the KEK
/// identifier included, fails decryption.
/// </remarks>
internal sealed class EncryptedObjectHeader
{
    private const int FixedPartSize = 6;

    private EncryptedObjectHeader(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>Gets the serialized header, used as associated data by every block.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets the per-object nonce prefix.</summary>
    public ReadOnlySpan<byte> NoncePrefix => Bytes.AsSpan(Bytes.Length - EnvelopeFormat.NoncePrefixSize);

    /// <summary>
    /// Returns whether a KEK identifier fits the header (non-empty, at most 255 UTF-8 bytes).
    /// </summary>
    /// <param name="kekId">The identifier to test.</param>
    /// <returns><c>true</c> when it can be written.</returns>
    public static bool IsValidKekId(string? kekId) =>
        !string.IsNullOrEmpty(kekId) && Encoding.UTF8.GetByteCount(kekId) <= EnvelopeFormat.MaxKekIdBytes;

    /// <summary>
    /// Creates a version 1 header with a fresh random nonce prefix.
    /// </summary>
    /// <param name="kekId">The identifier of the KEK that wrapped the object's data key. Must be valid.</param>
    /// <returns>The header.</returns>
    public static EncryptedObjectHeader Create(string kekId)
    {
        var kekIdLength = Encoding.UTF8.GetByteCount(kekId);
        var bytes = new byte[FixedPartSize + kekIdLength + EnvelopeFormat.NoncePrefixSize];

        EnvelopeFormat.Magic.CopyTo(bytes);
        bytes[4] = EnvelopeFormat.Version1;
        bytes[5] = (byte)kekIdLength;
        Encoding.UTF8.GetBytes(kekId, bytes.AsSpan(FixedPartSize, kekIdLength));
        RandomNumberGenerator.Fill(bytes.AsSpan(FixedPartSize + kekIdLength));

        return new EncryptedObjectHeader(bytes);
    }

    /// <summary>
    /// Reads and checks a header from the start of a stored object.
    /// </summary>
    /// <param name="source">The ciphertext stream, positioned at the start of the object.</param>
    /// <param name="storageKey">The storage key, for error messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The header, or an error when the object is not a supported encrypted object.</returns>
    public static async Task<Result<EncryptedObjectHeader>> ReadAsync(
        Stream source,
        string storageKey,
        CancellationToken cancellationToken)
    {
        var fixedPart = new byte[FixedPartSize];
        if (await source.ReadAtLeastAsync(fixedPart, FixedPartSize, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false) < FixedPartSize
            || !fixedPart.AsSpan(0, 4).SequenceEqual(EnvelopeFormat.Magic))
        {
            return StorageEncryptionErrors.InvalidHeader(storageKey);
        }

        if (fixedPart[4] != EnvelopeFormat.Version1)
        {
            return StorageEncryptionErrors.UnsupportedVersion(storageKey, fixedPart[4]);
        }

        var kekIdLength = fixedPart[5];
        if (kekIdLength == 0)
        {
            return StorageEncryptionErrors.InvalidHeader(storageKey);
        }

        var bytes = new byte[FixedPartSize + kekIdLength + EnvelopeFormat.NoncePrefixSize];
        fixedPart.CopyTo(bytes, 0);
        var rest = bytes.AsMemory(FixedPartSize);
        if (await source.ReadAtLeastAsync(rest, rest.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false) < rest.Length)
        {
            return StorageEncryptionErrors.InvalidHeader(storageKey);
        }

        return new EncryptedObjectHeader(bytes);
    }
}
