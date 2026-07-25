// -----------------------------------------------------------------------
// <copyright file="HttpResponseStream.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// A read-only stream decorator that owns both the underlying content stream and the
/// <see cref="HttpResponseMessage"/> it was read from. Disposing it disposes both, so
/// callers can treat a streamed download as a single disposable resource
/// (via <c>ObjectStream</c>) without leaking the HTTP response.
/// </summary>
internal sealed class HttpResponseStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpResponseMessage _response;

    public HttpResponseStream(Stream inner, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(response);
        _inner = inner;
        _response = response;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
