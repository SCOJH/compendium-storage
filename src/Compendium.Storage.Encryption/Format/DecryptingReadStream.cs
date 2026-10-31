// -----------------------------------------------------------------------
// <copyright file="DecryptingReadStream.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Security.Cryptography;

namespace Compendium.Storage.Encryption.Format;

/// <summary>
/// A read-only, forward-only stream that yields the plaintext of an encrypted object, verifying one block at a
/// time. Memory use is two pooled block buffers, whatever the object length.
/// </summary>
/// <remarks>
/// A block's plaintext is returned only after its tag verified. The sealed buffer holds one byte more than a
/// sealed block: the look-ahead byte tells whether the block just read is the last one, which is part of its
/// nonce, so an object cut at a block boundary or extended past its last block fails verification.
/// </remarks>
internal sealed class DecryptingReadStream : Stream
{
    private readonly Stream _source;
    private readonly AesGcm _aes;
    private readonly EncryptedObjectHeader _header;
    private byte[]? _sealed;
    private byte[]? _plaintext;
    private int _sealedCount;
    private int _plaintextOffset;
    private int _plaintextCount;
    private uint _counter;
    private bool _sourceExhausted;
    private bool _lastOpened;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecryptingReadStream"/> class.
    /// </summary>
    /// <param name="source">The ciphertext, positioned right after the header. Ownership is transferred.</param>
    /// <param name="aes">The data-key cipher. Ownership is transferred to this instance.</param>
    /// <param name="header">The object header, authenticated by every block.</param>
    public DecryptingReadStream(Stream source, AesGcm aes, EncryptedObjectHeader header)
    {
        _source = source;
        _aes = aes;
        _header = header;
        _sealed = ArrayPool<byte>.Shared.Rent(EnvelopeFormat.SealedBlockSize + 1);
        _plaintext = ArrayPool<byte>.Shared.Rent(EnvelopeFormat.BlockSize);
    }

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

    /// <summary>
    /// Reads and verifies the first block, so that a wrong key or an altered header is reported before any
    /// content is handed out.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the first block is verified.</returns>
    /// <exception cref="CryptographicException">The first block failed verification.</exception>
    public async ValueTask PrefetchAsync(CancellationToken cancellationToken)
    {
        await FillAsync(cancellationToken).ConfigureAwait(false);
        OpenBlock();
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

        if (_plaintextOffset == _plaintextCount)
        {
            if (_lastOpened)
            {
                return 0;
            }

            while (!_sourceExhausted && _sealedCount < EnvelopeFormat.SealedBlockSize + 1)
            {
                var read = _source.Read(_sealed.AsSpan(_sealedCount, EnvelopeFormat.SealedBlockSize + 1 - _sealedCount));
                Account(read);
            }

            OpenBlock();
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

        if (_plaintextOffset == _plaintextCount)
        {
            if (_lastOpened)
            {
                return 0;
            }

            await FillAsync(cancellationToken).ConfigureAwait(false);
            OpenBlock();
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
    public override async ValueTask DisposeAsync()
    {
        if (_sealed is not null)
        {
            await _source.DisposeAsync().ConfigureAwait(false);
            Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _sealed is not null)
        {
            _source.Dispose();
            Release();
        }

        base.Dispose(disposing);
    }

    private async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        while (!_sourceExhausted && _sealedCount < EnvelopeFormat.SealedBlockSize + 1)
        {
            var read = await _source
                .ReadAsync(_sealed.AsMemory(_sealedCount, EnvelopeFormat.SealedBlockSize + 1 - _sealedCount), cancellationToken)
                .ConfigureAwait(false);
            Account(read);
        }
    }

    private void Account(int read)
    {
        if (read == 0)
        {
            _sourceExhausted = true;
        }
        else
        {
            _sealedCount += read;
        }
    }

    private void OpenBlock()
    {
        var isLast = _sealedCount <= EnvelopeFormat.SealedBlockSize;
        var length = isLast ? _sealedCount : EnvelopeFormat.SealedBlockSize;
        if (length < EnvelopeFormat.TagSize)
        {
            throw EnvelopeFormat.IntegrityFailure();
        }

        var plaintextLength = length - EnvelopeFormat.TagSize;
        Span<byte> nonce = stackalloc byte[EnvelopeFormat.NonceSize];
        EnvelopeFormat.WriteNonce(_header.NoncePrefix, _counter, isLast, nonce);

        try
        {
            _aes.Decrypt(
                nonce,
                _sealed.AsSpan(0, plaintextLength),
                _sealed.AsSpan(plaintextLength, EnvelopeFormat.TagSize),
                _plaintext.AsSpan(0, plaintextLength),
                _header.Bytes);
        }
        catch (CryptographicException)
        {
            throw EnvelopeFormat.IntegrityFailure();
        }

        _plaintextOffset = 0;
        _plaintextCount = plaintextLength;

        if (isLast)
        {
            _lastOpened = true;
            _sealedCount = 0;
        }
        else
        {
            // A writer never produces more blocks than the counter can number: wrapping would reuse nonces.
            if (_counter == uint.MaxValue)
            {
                throw EnvelopeFormat.IntegrityFailure();
            }

            // Carry the look-ahead byte over to the next block.
            _sealed![0] = _sealed[EnvelopeFormat.SealedBlockSize];
            _sealedCount = 1;
            _counter++;
        }
    }

    private int Drain(Span<byte> destination)
    {
        var count = Math.Min(destination.Length, _plaintextCount - _plaintextOffset);
        _plaintext.AsSpan(_plaintextOffset, count).CopyTo(destination);
        _plaintextOffset += count;
        return count;
    }

    private void Release()
    {
        _aes.Dispose();
        ArrayPool<byte>.Shared.Return(_sealed!, clearArray: true);
        ArrayPool<byte>.Shared.Return(_plaintext!, clearArray: true);
        _sealed = null;
        _plaintext = null;
    }
}
