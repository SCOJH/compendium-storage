// -----------------------------------------------------------------------
// <copyright file="ConstantMemoryTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Abstractions.Storage.Models;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests;

/// <summary>
/// Runs alone: allocations are measured process-wide, so no other test may allocate meanwhile.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConstantMemoryCollection
{
    public const string Name = "Constant memory";
}

[Collection(ConstantMemoryCollection.Name)]
public sealed class ConstantMemoryTests
{
    private const long HundredMiB = 100L * 1024 * 1024;

    // Two pooled 64 KiB block buffers per stream plus small objects; anything near the payload
    // size would mean the content is buffered. 2 MiB is 2 % of the payload.
    private const long AllocationBudget = 2L * 1024 * 1024;

    [Fact]
    public async Task Put_100MiB_AllocatesConstantMemory()
    {
        // Arrange
        var sink = new DiscardingObjectStore();
        var harness = new EncryptionHarness(sink);
        await harness.Store.PutAsync("warm-up.bin", new PatternStream(1024 * 1024));

        // Act
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var put = await harness.Store.PutAsync("big.bin", new PatternStream(HundredMiB));
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Assert
        put.IsSuccess.Should().BeTrue();
        put.Value!.PlaintextLength.Should().Be(HundredMiB);
        sink.Consumed.Should().Be(EncryptionHarness.HeaderLength(put.Value.KekId) + HundredMiB + (1600 * 16));
        allocated.Should().BeLessThan(AllocationBudget);
    }

    [Fact]
    public async Task OpenRead_100MiB_RoundTripsWithConstantMemory()
    {
        // Arrange
        var harness = new EncryptionHarness();
        var stored = await harness.PutPatternAsync("big.bin", HundredMiB);
        var warmUp = await harness.PutPatternAsync("warm-up.bin", 1024 * 1024);
        await DrainAndVerifyAsync(harness, warmUp, new byte[81920]);
        var buffer = new byte[81920];

        // Act
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var verified = await DrainAndVerifyAsync(harness, stored, buffer);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Assert
        verified.Should().Be(HundredMiB);
        allocated.Should().BeLessThan(AllocationBudget);
    }

    private static async Task<long> DrainAndVerifyAsync(EncryptionHarness harness, EncryptedObject stored, byte[] buffer)
    {
        var open = await harness.Store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);
        open.IsSuccess.Should().BeTrue();
        await using var stream = open.Value!;

        var position = 0L;
        int read;
        while ((read = await stream.Content.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != PatternStream.ByteAt(position + i))
                {
                    throw new InvalidOperationException($"Mismatch at byte {position + i}.");
                }
            }

            position += read;
        }

        return position;
    }

    /// <summary>
    /// An <see cref="IObjectStore"/> that reads uploads to the end through one reused buffer and keeps nothing.
    /// </summary>
    private sealed class DiscardingObjectStore : IObjectStore
    {
        private readonly byte[] _buffer = new byte[81920];

        public long Consumed { get; private set; }

        public async Task<Result<ObjectInfo>> PutAsync(
            string key,
            Stream content,
            ObjectMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Consumed = 0;
            int read;
            while ((read = await content.ReadAsync(_buffer, cancellationToken)) > 0)
            {
                Consumed += read;
            }

            return new ObjectInfo(key, Consumed, "etag", null, DateTimeOffset.UnixEpoch);
        }

        public Task<Result<ObjectStream>> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ListPage>> ListAsync(ListOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<Uri>> GetPresignedUrlAsync(
            string key,
            PresignedAction action,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
