// -----------------------------------------------------------------------
// <copyright file="PatternStream.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Storage.Encryption.Tests.TestSupport;

/// <summary>
/// A non-seekable stream of <c>length</c> deterministic bytes, generated on the fly so that large payloads cost
/// no memory, and the matching <see cref="ByteAt"/> used to verify decrypted output without holding a copy.
/// </summary>
internal sealed class PatternStream(long length, int maxChunk = int.MaxValue) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static byte ByteAt(long position) => (byte)((position * 31) ^ (position >> 9));

    public static byte[] Bytes(long length)
    {
        var bytes = new byte[length];
        for (var i = 0L; i < length; i++)
        {
            bytes[i] = ByteAt(i);
        }

        return bytes;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = (int)Math.Min(Math.Min(buffer.Length, maxChunk), length - _position);
        for (var i = 0; i < count; i++)
        {
            buffer[i] = ByteAt(_position + i);
        }

        _position += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
