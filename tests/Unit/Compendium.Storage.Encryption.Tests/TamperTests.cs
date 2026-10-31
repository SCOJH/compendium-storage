// -----------------------------------------------------------------------
// <copyright file="TamperTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests;

/// <summary>
/// Every change to a stored object — one byte, one block removed, moved or duplicated, a truncation, an
/// extension — must be refused, and no unverified plaintext may be handed out before the refusal.
/// </summary>
public sealed class TamperTests
{
    private const int Block = EncryptionHarness.Block;
    private const int SealedBlock = EncryptionHarness.SealedBlock;

    // Four blocks: three full ones and a 100-byte last block.
    private const int Length = (3 * Block) + 100;

    private readonly EncryptionHarness _harness = new();

    public enum Region
    {
        Magic,
        Version,
        KekIdLength,
        KekId,
        NoncePrefix,
        FirstBlockCiphertext,
        FirstBlockTag,
        MiddleBlockCiphertext,
        MiddleBlockTag,
        LastBlockCiphertext,
        LastBlockTag,
    }

    [Theory]
    [InlineData(Region.Magic, StorageEncryptionErrors.Prefix + ".InvalidHeader")]
    [InlineData(Region.Version, StorageEncryptionErrors.Prefix + ".UnsupportedVersion")]
    [InlineData(Region.KekIdLength, StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed")]
    [InlineData(Region.KekId, StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed")]
    [InlineData(Region.NoncePrefix, StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed")]
    [InlineData(Region.FirstBlockCiphertext, StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed")]
    [InlineData(Region.FirstBlockTag, StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed")]
    public async Task OpenRead_HeaderOrFirstBlockByteFlipped_ReturnsError(Region region, string expectedCode)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        raw[Offset(stored, region)] ^= 0x01;
        _harness.Memory["doc.bin"] = raw;

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.IsFailure.Should().BeTrue();
        outcome.Open.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(Region.MiddleBlockCiphertext, 1)]
    [InlineData(Region.MiddleBlockTag, 1)]
    [InlineData(Region.LastBlockCiphertext, 3)]
    [InlineData(Region.LastBlockTag, 3)]
    public async Task Read_LaterBlockByteFlipped_ThrowsAfterOnlyVerifiedBlocks(Region region, int verifiedBlocks)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        raw[Offset(stored, region)] ^= 0x80;
        _harness.Memory["doc.bin"] = raw;

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.IsSuccess.Should().BeTrue();
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(verifiedBlocks * Block));
    }

    [Fact]
    public async Task Read_EveryByteOfSmallObjectFlipped_IsAlwaysRefused()
    {
        // Arrange
        var plaintext = PatternStream.Bytes(40);
        var stored = await _harness.PutAsync("doc.bin", plaintext);
        var original = _harness.Memory["doc.bin"];

        for (var i = 0; i < original.Length; i++)
        {
            var tampered = (byte[])original.Clone();
            tampered[i] ^= 0x04;
            _harness.Memory["doc.bin"] = tampered;

            // Act
            var outcome = await _harness.ReadAsync(stored);

            // Assert
            outcome.Succeeded.Should().BeFalse($"byte {i} was modified");
            outcome.Content.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Read_MiddleBlockRemoved_Throws()
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"].ToList();
        raw.RemoveRange(BlockStart(stored, 1), SealedBlock);
        _harness.Memory["doc.bin"] = [.. raw];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(Block));
    }

    [Fact]
    public async Task Read_FirstBlockRemoved_ReturnsError()
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"].ToList();
        raw.RemoveRange(BlockStart(stored, 0), SealedBlock);
        _harness.Memory["doc.bin"] = [.. raw];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(0, 2)]
    public async Task Read_BlocksSwapped_IsRefused(int first, int second)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        var a = raw.AsSpan(BlockStart(stored, first), SealedBlock).ToArray();
        var b = raw.AsSpan(BlockStart(stored, second), SealedBlock).ToArray();
        a.CopyTo(raw, BlockStart(stored, second));
        b.CopyTo(raw, BlockStart(stored, first));
        _harness.Memory["doc.bin"] = raw;

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Succeeded.Should().BeFalse();
        outcome.Content.Should().Equal(PatternStream.Bytes(first * Block));
    }

    [Fact]
    public async Task Read_BlockDuplicated_Throws()
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"].ToList();
        var copy = raw.GetRange(BlockStart(stored, 1), SealedBlock);
        raw.InsertRange(BlockStart(stored, 2), copy);
        _harness.Memory["doc.bin"] = [.. raw];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(2 * Block));
    }

    [Fact]
    public async Task Read_LastBlockDropped_Throws()
    {
        // Arrange — the object now ends on a block boundary, its last block flagged "not last".
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        _harness.Memory["doc.bin"] = raw[..BlockStart(stored, 3)];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(2 * Block));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(116)]
    public async Task Read_LastBlockTruncated_Throws(int removed)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        _harness.Memory["doc.bin"] = raw[..^removed];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Length.Should().BeLessThanOrEqualTo(3 * Block);
    }

    [Fact]
    public async Task Read_FullLastBlockDropped_Throws()
    {
        // Arrange — exactly two full blocks: dropping the last leaves a full block that was not the last.
        var stored = await _harness.PutPatternAsync("doc.bin", 2 * Block);
        _harness.Memory["doc.bin"] = _harness.Memory["doc.bin"][..BlockStart(stored, 1)];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(SealedBlock)]
    public async Task Read_DataAppended_IsRefused(int appended)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        _harness.Memory["doc.bin"] = [.. raw, .. new byte[appended]];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(3 * Block));
    }

    [Fact]
    public async Task Read_LastBlockRepeated_IsRefused()
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", Length);
        var raw = _harness.Memory["doc.bin"];
        _harness.Memory["doc.bin"] = [.. raw, .. raw[BlockStart(stored, 3)..]];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(10)]
    public async Task OpenRead_TruncatedInsideHeader_ReturnsInvalidHeader(int kept)
    {
        // Arrange
        var stored = await _harness.PutPatternAsync("doc.bin", 10);
        _harness.Memory["doc.bin"] = _harness.Memory["doc.bin"][..kept];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidHeader");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public async Task OpenRead_HeaderWithoutWholeTag_ReturnsIntegrityError(int bodyBytes)
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", []);
        var header = EncryptionHarness.HeaderLength(stored.KekId);
        _harness.Memory["doc.bin"] = _harness.Memory["doc.bin"][..(header + bodyBytes)];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    [Fact]
    public async Task OpenRead_ZeroKekIdLengthInHeader_ReturnsInvalidHeader()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1]);
        var raw = _harness.Memory["doc.bin"];
        raw[5] = 0;
        _harness.Memory["doc.bin"] = raw;

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidHeader");
    }

    [Fact]
    public async Task OpenRead_PlaintextObject_ReturnsInvalidHeader()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1]);
        _harness.Memory["doc.bin"] = PatternStream.Bytes(1000);

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidHeader");
    }

    [Fact]
    public async Task OpenRead_BlockSplicedFromAnotherObject_IsRefused()
    {
        // Arrange — same tenant, same KEK, same length: only the data key and nonce prefix differ.
        var a = await _harness.PutPatternAsync("a.bin", Length);
        await _harness.PutPatternAsync("b.bin", Length);
        var rawA = _harness.Memory["a.bin"];
        var rawB = _harness.Memory["b.bin"];
        rawB.AsSpan(BlockStart(a, 1), SealedBlock).CopyTo(rawA.AsSpan(BlockStart(a, 1)));
        _harness.Memory["a.bin"] = rawA;

        // Act
        var outcome = await _harness.ReadAsync(a);

        // Assert
        outcome.ReadError.Should().BeAssignableTo<CryptographicException>();
        outcome.Content.Should().Equal(PatternStream.Bytes(Block));
    }

    [Fact]
    public async Task OpenRead_WholeObjectSwappedWithAnother_IsRefused()
    {
        // Arrange
        var a = await _harness.PutPatternAsync("a.bin", 100);
        await _harness.PutPatternAsync("b.bin", 100);
        _harness.Memory["a.bin"] = _harness.Memory["b.bin"];

        // Act
        var outcome = await _harness.ReadAsync(a);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    [Fact]
    public async Task OpenRead_EmptyObjectWithDataAppended_IsRefused()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", []);
        _harness.Memory["doc.bin"] = [.. _harness.Memory["doc.bin"], 0];

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    private static int BlockStart(EncryptedObject stored, int block) =>
        EncryptionHarness.HeaderLength(stored.KekId) + (block * SealedBlock);

    private static int Offset(EncryptedObject stored, Region region) => region switch
    {
        Region.Magic => 0,
        Region.Version => 4,
        Region.KekIdLength => 5,
        Region.KekId => 6,
        Region.NoncePrefix => EncryptionHarness.HeaderLength(stored.KekId) - 1,
        Region.FirstBlockCiphertext => BlockStart(stored, 0) + 10,
        Region.FirstBlockTag => BlockStart(stored, 0) + Block + 3,
        Region.MiddleBlockCiphertext => BlockStart(stored, 1) + 1000,
        Region.MiddleBlockTag => BlockStart(stored, 1) + Block + 15,
        Region.LastBlockCiphertext => BlockStart(stored, 3) + 99,
        Region.LastBlockTag => BlockStart(stored, 3) + 100,
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };
}
