// -----------------------------------------------------------------------
// <copyright file="RoundTripTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Compendium.Abstractions.Storage.Models;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests;

public sealed class RoundTripTests
{
    private const int Block = EncryptionHarness.Block;

    private readonly EncryptionHarness _harness = new();

    public static TheoryData<int> Lengths => new()
    {
        0,
        1,
        15,
        16,
        17,
        Block - 1,
        Block,
        Block + 1,
        (2 * Block) - 1,
        2 * Block,
        (3 * Block) + 5,
    };

    [Theory]
    [MemberData(nameof(Lengths))]
    public async Task PutAndOpenRead_AnyLength_ReturnsOriginalContent(int length)
    {
        // Arrange
        var plaintext = PatternStream.Bytes(length);

        // Act
        var stored = await _harness.PutAsync("doc.bin", plaintext);
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Succeeded.Should().BeTrue();
        outcome.Content.Should().Equal(plaintext);
        stored.PlaintextLength.Should().Be(length);
        outcome.Open.Value!.Info.Size.Should().Be(length);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public async Task Put_AnyLength_StoresHeaderPlusOneTagPerBlock(int length)
    {
        // Arrange
        var expectedBlocks = Math.Max(1, (length + Block - 1) / Block);

        // Act
        var stored = await _harness.PutAsync("doc.bin", PatternStream.Bytes(length));

        // Assert
        var raw = _harness.Memory["doc.bin"];
        raw.Length.Should().Be(EncryptionHarness.HeaderLength(stored.KekId) + length + (16 * expectedBlocks));
        stored.Info.Size.Should().Be(raw.Length);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(4096, true)]
    [InlineData(Block + 3, true)]
    public async Task OpenRead_SmallOrOddReads_ReturnsOriginalContent(int chunk, bool synchronous)
    {
        // Arrange
        var plaintext = PatternStream.Bytes((2 * Block) + 11);
        var stored = await _harness.PutAsync("doc.bin", plaintext);

        // Act
        var outcome = await _harness.ReadAsync(stored, chunk, synchronous);

        // Assert
        outcome.Succeeded.Should().BeTrue();
        outcome.Content.Should().Equal(plaintext);
    }

    [Fact]
    public async Task Put_SynchronousReadingStore_ProducesReadableObject()
    {
        // Arrange
        _harness.Memory.UseSynchronousReads = true;
        var plaintext = PatternStream.Bytes((2 * Block) + 1);

        // Act
        var stored = await _harness.PutAsync("doc.bin", plaintext);
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Succeeded.Should().BeTrue();
        outcome.Content.Should().Equal(plaintext);
    }

    [Fact]
    public async Task Put_SourceYieldingTinyChunks_FillsWholeBlocks()
    {
        // Arrange
        var length = (2 * Block) + 9;

        // Act
        var put = await _harness.Store.PutAsync("doc.bin", new PatternStream(length, maxChunk: 1000));
        var outcome = await _harness.ReadAsync(put.Value!);

        // Assert
        _harness.Memory["doc.bin"].Length.Should().Be(EncryptionHarness.HeaderLength(put.Value!.KekId) + length + (3 * 16));
        outcome.Content.Should().Equal(PatternStream.Bytes(length));
    }

    [Fact]
    public async Task Put_ReturnsKeyWrappedDataKeyAndKekIdToCaller()
    {
        // Act
        var stored = await _harness.PutAsync("folder/doc.bin", [1, 2, 3]);

        // Assert
        stored.StorageKey.Should().Be("folder/doc.bin");
        stored.KekId.Should().Be(_harness.KeyProvider.CurrentKekId(EncryptionHarness.TenantA));
        stored.WrappedDek.Length.Should().BeGreaterThan(32);
        stored.Info.Key.Should().Be("folder/doc.bin");
    }

    [Fact]
    public async Task Put_StoresVersionedHeaderWithKekIdAndNoKeyMaterial()
    {
        // Act
        var stored = await _harness.PutAsync("doc.bin", PatternStream.Bytes(100));

        // Assert
        var raw = _harness.Memory["doc.bin"];
        Encoding.ASCII.GetString(raw, 0, 4).Should().Be("CPSE");
        raw[4].Should().Be(1);
        raw[5].Should().Be((byte)stored.KekId.Length);
        Encoding.UTF8.GetString(raw, 6, stored.KekId.Length).Should().Be(stored.KekId);
        Contains(raw, _harness.KeyProvider.LastDataKeyCopy!).Should().BeFalse();
        Contains(raw, stored.WrappedDek.ToArray()).Should().BeFalse();
        Contains(raw, PatternStream.Bytes(100)).Should().BeFalse();
    }

    [Fact]
    public async Task Put_SameContentTwice_UsesFreshDataKeyAndNonces()
    {
        // Arrange
        var plaintext = PatternStream.Bytes(1000);

        // Act
        var first = await _harness.PutAsync("a.bin", plaintext);
        var second = await _harness.PutAsync("b.bin", plaintext);

        // Assert
        first.WrappedDek.ToArray().Should().NotEqual(second.WrappedDek.ToArray());
        _harness.Memory["a.bin"].Should().NotEqual(_harness.Memory["b.bin"]);
        var header = EncryptionHarness.HeaderLength(first.KekId);
        _harness.Memory["a.bin"].AsSpan(header - 7, 7).ToArray()
            .Should().NotEqual(_harness.Memory["b.bin"].AsSpan(header - 7, 7).ToArray());
    }

    [Fact]
    public async Task Put_PassesMetadataThroughToInnerStore()
    {
        // Act
        var put = await _harness.Store.PutAsync(
            "doc.bin",
            new MemoryStream([1]),
            new ObjectMetadata(ContentType: "application/octet-stream"));

        // Assert
        put.Value!.Info.ContentType.Should().Be("application/octet-stream");
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
