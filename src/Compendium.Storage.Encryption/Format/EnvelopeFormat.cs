// -----------------------------------------------------------------------
// <copyright file="EnvelopeFormat.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Compendium.Storage.Encryption.Format;

/// <summary>
/// Constants and nonce layout of the version 1 envelope format.
/// </summary>
/// <remarks>
/// <para>
/// An object is a header (see <see cref="EncryptedObjectHeader"/>) followed by blocks. Each block is up to
/// <see cref="BlockSize"/> plaintext bytes encrypted with AES-256-GCM, followed by its 16-byte tag; every block
/// but the last carries exactly <see cref="BlockSize"/> plaintext bytes, and there is always a last block (an
/// empty object is one empty last block).
/// </para>
/// <para>
/// The 12-byte nonce of block <c>i</c> is <c>prefix (7 bytes) || i (4 bytes, big endian) || last (1 byte)</c>,
/// the STREAM construction: a block moved to another position, or a non-last block presented as the last one,
/// fails authentication, so reordering, removal and truncation are all detected. Every block authenticates
/// the whole header followed by the owning tenant's identifier as associated data.
/// </para>
/// </remarks>
internal static class EnvelopeFormat
{
    /// <summary>The format version written by this library.</summary>
    public const byte Version1 = 1;

    /// <summary>Plaintext bytes per block.</summary>
    public const int BlockSize = 64 * 1024;

    /// <summary>AES-GCM tag size, in bytes.</summary>
    public const int TagSize = 16;

    /// <summary>Ciphertext bytes of a full block, tag included.</summary>
    public const int SealedBlockSize = BlockSize + TagSize;

    /// <summary>AES-256 key size, in bytes.</summary>
    public const int DataKeySize = 32;

    /// <summary>AES-GCM nonce size, in bytes.</summary>
    public const int NonceSize = 12;

    /// <summary>Random per-object nonce prefix size, in bytes.</summary>
    public const int NoncePrefixSize = 7;

    /// <summary>Maximum UTF-8 length of a KEK identifier, in bytes.</summary>
    public const int MaxKekIdBytes = 255;

    /// <summary>Magic number opening every encrypted object.</summary>
    public static ReadOnlySpan<byte> Magic => "CPSE"u8;

    /// <summary>
    /// Writes the nonce of a block.
    /// </summary>
    /// <param name="noncePrefix">The object's nonce prefix.</param>
    /// <param name="counter">The zero-based block index.</param>
    /// <param name="isLast">Whether the block is the last of the object.</param>
    /// <param name="destination">A <see cref="NonceSize"/>-byte destination.</param>
    public static void WriteNonce(ReadOnlySpan<byte> noncePrefix, uint counter, bool isLast, Span<byte> destination)
    {
        noncePrefix.CopyTo(destination);
        BinaryPrimitives.WriteUInt32BigEndian(destination[NoncePrefixSize..], counter);
        destination[NonceSize - 1] = isLast ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Returns the plaintext length of an object from its stored length, assuming a well-formed object.
    /// </summary>
    /// <param name="storedLength">The ciphertext length, header included.</param>
    /// <param name="headerLength">The header length.</param>
    /// <returns>The plaintext length, or <c>0</c> when <paramref name="storedLength"/> cannot hold an object.</returns>
    public static long PlaintextLength(long storedLength, int headerLength)
    {
        var body = storedLength - headerLength;
        if (body < TagSize)
        {
            return 0;
        }

        var blocks = (body + SealedBlockSize - 1) / SealedBlockSize;
        return body - (blocks * TagSize);
    }

    /// <summary>
    /// The exception raised when a block fails verification. Its message never carries key or content data.
    /// </summary>
    /// <returns>A new <see cref="CryptographicException"/>.</returns>
    public static CryptographicException IntegrityFailure() =>
        new("The encrypted object failed integrity verification: it was modified, truncated or reordered.");
}
