// -----------------------------------------------------------------------
// <copyright file="StreamingSupport.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Core.Results;

namespace Compendium.Adapters.S3.IntegrationTests.TestSupport;

/// <summary>
/// A non-seekable stream of deterministic bytes generated on the fly: large payloads cost no memory.
/// </summary>
internal sealed class PatternStream(long length) : Stream
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

    /// <summary>Reads <paramref name="source"/> to its end, checking every byte against the pattern.</summary>
    public static async Task<long> VerifyAsync(Stream source)
    {
        var buffer = new byte[81920];
        var position = 0L;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != ByteAt(position + i))
                {
                    throw new InvalidOperationException($"Mismatch at byte {position + i}.");
                }
            }

            position += read;
        }

        return position;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, length - _position);
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

/// <summary>
/// Samples the live managed heap (after a full collection) while an operation runs, and reports the peak
/// above the level measured just before it. Garbage is excluded, so this is retained memory, not allocation.
/// </summary>
internal sealed class LiveMemoryProbe
{
    private readonly long _baseline;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampler;
    private long _peak;

    private LiveMemoryProbe()
    {
        _baseline = GC.GetTotalMemory(forceFullCollection: true);
        _peak = _baseline;
        _sampler = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                Sample();
                await Task.Delay(20).ConfigureAwait(false);
            }
        });
    }

    public static LiveMemoryProbe Start() => new();

    /// <summary>Stops sampling and returns the peak live heap above the baseline, in bytes.</summary>
    public async Task<long> StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _sampler.ConfigureAwait(false);
        Sample();
        _stop.Dispose();
        return Interlocked.Read(ref _peak) - _baseline;
    }

    private void Sample()
    {
        var live = GC.GetTotalMemory(forceFullCollection: true);
        long current;
        while (live > (current = Interlocked.Read(ref _peak))
               && Interlocked.CompareExchange(ref _peak, live, current) != current)
        {
        }
    }
}

/// <summary>
/// Test <see cref="IKeyEncryptionKeyProvider"/>: one random in-memory KEK, data keys wrapped with AES-GCM
/// bound to the tenant and KEK identifier. Generated per run, never fixed.
/// </summary>
internal sealed class InMemoryKeyProvider : IKeyEncryptionKeyProvider
{
    private const string KekId = "kek-test-1";
    private readonly byte[] _kek = RandomNumberGenerator.GetBytes(32);

    public Task<Result<WrappedDataKey>> WrapAsync(
        string tenantId,
        ReadOnlyMemory<byte> dataKey,
        CancellationToken cancellationToken = default)
    {
        var wrapped = new byte[12 + dataKey.Length + 16];
        RandomNumberGenerator.Fill(wrapped.AsSpan(0, 12));
        using var aes = new AesGcm(_kek, 16);
        aes.Encrypt(
            wrapped.AsSpan(0, 12),
            dataKey.Span,
            wrapped.AsSpan(12, dataKey.Length),
            wrapped.AsSpan(12 + dataKey.Length),
            Binding(tenantId, KekId));
        return Task.FromResult(Result.Success(new WrappedDataKey(KekId, wrapped)));
    }

    public Task<Result<byte[]>> UnwrapAsync(
        string tenantId,
        string kekId,
        ReadOnlyMemory<byte> wrappedKey,
        CancellationToken cancellationToken = default)
    {
        var wrapped = wrappedKey.Span;
        var dataKey = new byte[wrapped.Length - 28];
        using var aes = new AesGcm(_kek, 16);
        aes.Decrypt(wrapped[..12], wrapped[12..^16], wrapped[^16..], dataKey, Binding(tenantId, kekId));
        return Task.FromResult(Result.Success(dataKey));
    }

    private static byte[] Binding(string tenantId, string kekId) => Encoding.UTF8.GetBytes($"{tenantId}\n{kekId}");
}
