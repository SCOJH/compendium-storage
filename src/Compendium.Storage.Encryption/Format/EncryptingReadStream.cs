// -----------------------------------------------------------------------
// <copyright file="EncryptingReadStream.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Security.Cryptography;

namespace Compendium.Storage.Encryption.Format;

/// <summary>
/// A read-only, forward-only stream that yields the encrypted form (header then sealed blocks) of a plaintext
/// source, encrypting one block at a time. Memory use is two pooled block buffers, whatever the source length.
/// </summary>
/// <remarks>
/// The plaintext buffer holds one byte more than a block: the look-ahead byte tells whether the block just
/// filled is the last one, so the last-block flag is set without knowing the source length in advance.
/// The source is not disposed: it belongs to the caller.
/// </remarks>
internal sealed class EncryptingReadStream : Stream
{
    private readonly Stream _source;
    private readonly AesGcm _aes;
    private readonly EncryptedObjectHeader _header;
    private byte[]? _plaintext;
    private byte[]? _sealed;
    private int _plaintextCount;
    private int _sealedOffset;
    private int _sealedCount;
    private uint _counter;
    private bool _sourceExhausted;
    private bool _lastSealed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptingReadStream"/> class.
    /// </summary>
    /// <param name="source">The plaintext source.</param>
    /// <param name="aes">The data-key cipher. Ownership is transferred to this instance.</param>
    /// <param name="header">The object header, emitted first and authenticated by every block.</param>
    public EncryptingReadStream(Stream source, AesGcm aes, EncryptedObjectHeader header)
    {
        _source = source;
        _aes = aes;
        _header = header;
        _plaintext = ArrayPool<byte>.Shared.Rent(EnvelopeFormat.BlockSize + 1);
        _sealed = ArrayPool<byte>.Shared.Rent(Math.Max(EnvelopeFormat.SealedBlockSize, header.Bytes.Length));

        header.Bytes.CopyTo(_sealed, 0);
        _sealedCount = header.Bytes.Length;
    }

    /// <summary>Gets the number of plaintext bytes encrypted so far.</summary>
    public long PlaintextLength { get; private set; }

    /// <summary>Gets a value indicating whether the whole encrypted object, last block included, was read.</summary>
    public bool Completed => _lastSealed && _sealedOffset == _sealedCount;

    /// <inheritdoc />
    public override bool CanRead => _sealed is not null;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_sealed is null, this);

        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_sealedOffset == _sealedCount)
        {
            if (_lastSealed)
            {
                return 0;
            }

            while (!_sourceExhausted && _plaintextCount < EnvelopeFormat.BlockSize + 1)
            {
                var read = _source.Read(_plaintext.AsSpan(_plaintextCount, EnvelopeFormat.BlockSize + 1 - _plaintextCount));
                Account(read);
            }

            SealBlock();
        }

        return Drain(buffer);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_sealed is null, this);

        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_sealedOffset == _sealedCount)
        {
            if (_lastSealed)
            {
                return 0;
            }

            while (!_sourceExhausted && _plaintextCount < EnvelopeFormat.BlockSize + 1)
            {
                var read = await _source
                    .ReadAsync(_plaintext.AsMemory(_plaintextCount, EnvelopeFormat.BlockSize + 1 - _plaintextCount), cancellationToken)
                    .ConfigureAwait(false);
                Account(read);
            }

            SealBlock();
        }

        return Drain(buffer.Span);
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _sealed is not null)
        {
            _aes.Dispose();
            ArrayPool<byte>.Shared.Return(_plaintext!, clearArray: true);
            ArrayPool<byte>.Shared.Return(_sealed, clearArray: true);
            _plaintext = null;
            _sealed = null;
        }

        base.Dispose(disposing);
    }

    private void Account(int read)
    {
        if (read == 0)
        {
            _sourceExhausted = true;
        }
        else
        {
            _plaintextCount += read;
        }
    }

    private void SealBlock()
    {
        var isLast = _plaintextCount <= EnvelopeFormat.BlockSize;
        var length = isLast ? _plaintextCount : EnvelopeFormat.BlockSize;

        if (!isLast && _counter == uint.MaxValue)
        {
            throw new InvalidOperationException("The object exceeds the maximum number of blocks of the format.");
        }

        Span<byte> nonce = stackalloc byte[EnvelopeFormat.NonceSize];
        EnvelopeFormat.WriteNonce(_header.NoncePrefix, _counter, isLast, nonce);
        _aes.Encrypt(
            nonce,
            _plaintext.AsSpan(0, length),
            _sealed.AsSpan(0, length),
            _sealed.AsSpan(length, EnvelopeFormat.TagSize),
            _header.Bytes);

        PlaintextLength += length;
        _sealedOffset = 0;
        _sealedCount = length + EnvelopeFormat.TagSize;

        if (isLast)
        {
            _lastSealed = true;
            _plaintextCount = 0;
        }
        else
        {
            // Carry the look-ahead byte over to the next block.
            _plaintext![0] = _plaintext[EnvelopeFormat.BlockSize];
            _plaintextCount = 1;
            _counter++;
        }
    }

    private int Drain(Span<byte> destination)
    {
        var count = Math.Min(destination.Length, _sealedCount - _sealedOffset);
        _sealed.AsSpan(_sealedOffset, count).CopyTo(destination);
        _sealedOffset += count;
        return count;
    }
}
