// -----------------------------------------------------------------------
// <copyright file="StreamSurfaceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Compendium.Storage.Encryption.Format;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests.Format;

public sealed class StreamSurfaceTests
{
    public static TheoryData<string> Streams => new() { "encrypting", "decrypting" };

    [Theory]
    [MemberData(nameof(Streams))]
    public void Stream_IsReadOnlyAndForwardOnly(string kind)
    {
        // Arrange
        using var stream = Create(kind);

        // Act
        var length = () => stream.Length;
        var getPosition = () => stream.Position;
        var setPosition = () => stream.Position = 0;
        var seek = () => stream.Seek(0, SeekOrigin.Begin);
        var setLength = () => stream.SetLength(1);
        var write = () => stream.Write([1], 0, 1);

        // Assert
        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeFalse();
        length.Should().Throw<NotSupportedException>();
        getPosition.Should().Throw<NotSupportedException>();
        setPosition.Should().Throw<NotSupportedException>();
        seek.Should().Throw<NotSupportedException>();
        setLength.Should().Throw<NotSupportedException>();
        write.Should().Throw<NotSupportedException>();
        stream.Invoking(s => s.Flush()).Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(Streams))]
    public async Task Stream_EmptyBuffer_ReadsNothing(string kind)
    {
        // Arrange
        await using var stream = Create(kind);

        // Act
        var sync = stream.Read([], 0, 0);
        var async = await stream.ReadAsync(Memory<byte>.Empty);

        // Assert
        sync.Should().Be(0);
        async.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Streams))]
    public async Task Stream_AfterDispose_RefusesReads(string kind)
    {
        // Arrange
        var stream = Create(kind);
        await stream.DisposeAsync();

        // Act
        var sync = () => stream.Read(new byte[1], 0, 1);
        var async = async () => await stream.ReadAsync(new byte[1], 0, 1);

        // Assert
        stream.CanRead.Should().BeFalse();
        sync.Should().Throw<ObjectDisposedException>();
        await async.Should().ThrowAsync<ObjectDisposedException>();
        stream.Dispose();
    }

    [Fact]
    public void EncryptingStream_ReadToEnd_ThenReturnsZero()
    {
        // Arrange
        using var stream = Create("encrypting");
        var sink = new MemoryStream();

        // Act
        stream.CopyTo(sink);
        var extra = stream.Read(new byte[10], 0, 10);

        // Assert
        sink.Length.Should().BeGreaterThan(0);
        extra.Should().Be(0);
        ((EncryptingReadStream)stream).Completed.Should().BeTrue();
    }

    [Fact]
    public void DecryptingStream_SynchronousReadOfTamperedBlock_Throws()
    {
        // Arrange — two blocks; the second is damaged.
        var (header, key, sealedBody) = Seal(PatternStream.Bytes(EncryptionHarness.Block + 10));
        sealedBody[^1] ^= 1;
        using var stream = new DecryptingReadStream(new MemoryStream(sealedBody), new AesGcm(key, 16), header);
        var buffer = new byte[EncryptionHarness.Block];

        // Act
        var first = stream.Read(buffer, 0, buffer.Length);
        var second = () => stream.Read(buffer, 0, buffer.Length);

        // Assert
        first.Should().Be(EncryptionHarness.Block);
        second.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task DecryptingStream_ReadAsyncArrayOverload_RoundTrips()
    {
        // Arrange
        var plaintext = PatternStream.Bytes(1000);
        var (header, key, sealedBody) = Seal(plaintext);
        await using var stream = new DecryptingReadStream(new MemoryStream(sealedBody), new AesGcm(key, 16), header);
        var buffer = new byte[2000];

        // Act
        var read = await stream.ReadAsync(buffer, 0, buffer.Length);

        // Assert
        buffer[..read].Should().Equal(plaintext);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100, 15)]
    public void PlaintextLength_StoredLengthTooShort_IsZero(int header, int body)
    {
        // Act
        var length = EnvelopeFormat.PlaintextLength(header + body, header);

        // Assert
        length.Should().Be(0);
    }

    private static Stream Create(string kind)
    {
        var (header, key, sealedBody) = Seal([1, 2, 3]);
        return kind == "encrypting"
            ? new EncryptingReadStream(new MemoryStream([1, 2, 3]), new AesGcm(key, 16), header)
            : new DecryptingReadStream(new MemoryStream(sealedBody), new AesGcm(key, 16), header);
    }

    private static (EncryptedObjectHeader Header, byte[] Key, byte[] SealedBody) Seal(byte[] plaintext)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var header = EncryptedObjectHeader.Create("kek-test");
        using var encrypting = new EncryptingReadStream(new MemoryStream(plaintext), new AesGcm(key, 16), header);
        var sink = new MemoryStream();
        encrypting.CopyTo(sink);
        return (header, key, sink.ToArray()[header.Bytes.Length..]);
    }
}
