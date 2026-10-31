// -----------------------------------------------------------------------
// <copyright file="KeyHandlingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests;

public sealed class KeyHandlingTests
{
    private readonly EncryptionHarness _harness = new();

    [Fact]
    public async Task OpenRead_UnknownKekId_ReturnsProviderError()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);

        // Act
        var outcome = await _harness.ReadAsync(stored.StorageKey, stored.WrappedDek, "kek-unknown");

        // Assert
        outcome.Open.Error.Code.Should().Be("Test.UnknownKek");
    }

    [Fact]
    public async Task OpenRead_AnotherTenantsKek_ReturnsProviderError()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        var otherKek = _harness.KeyProvider.CurrentKekId("tenant-b");

        // Act
        var outcome = await _harness.ReadAsync(stored.StorageKey, stored.WrappedDek, otherKek);

        // Assert
        outcome.Open.Error.Code.Should().Be("Test.UnwrapFailed");
    }

    [Fact]
    public async Task OpenRead_FromAnotherTenant_ReturnsProviderError()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        _harness.SetTenant("tenant-b");

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be("Test.UnwrapFailed");
    }

    [Fact]
    public async Task OpenRead_WrappedDekAltered_ReturnsProviderError()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        var wrapped = stored.WrappedDek.ToArray();
        wrapped[20] ^= 0x01;

        // Act
        var outcome = await _harness.ReadAsync(stored.StorageKey, wrapped, stored.KekId);

        // Assert
        outcome.Open.Error.Code.Should().Be("Test.UnwrapFailed");
    }

    [Fact]
    public async Task OpenRead_ProviderReturnsWrongDataKey_ReturnsIntegrityError()
    {
        // Arrange — a provider that unwraps "successfully" to the wrong key must still be caught.
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Result.Success(new WrappedDataKey("kek-1", new byte[40])));
        keys.UnwrapAsync(default!, default!, default).ReturnsForAnyArgs(_ => Result.Success(RandomNumberGenerator.GetBytes(32)));
        var harness = new EncryptionHarness(keys: keys);
        var stored = await harness.PutAsync("doc.bin", PatternStream.Bytes(70_000));

        // Act
        var outcome = await harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task OpenRead_ProviderReturnsDataKeyOfWrongSize_ReturnsInvalidDataKey(int size)
    {
        // Arrange
        var returned = new byte[size];
        Array.Fill(returned, (byte)0xAA);
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Result.Success(new WrappedDataKey("kek-1", new byte[40])));
        keys.UnwrapAsync(default!, default!, default).ReturnsForAnyArgs(Result.Success(returned));
        var harness = new EncryptionHarness(keys: keys);
        var stored = await harness.PutAsync("doc.bin", [1]);

        // Act
        var outcome = await harness.ReadAsync(stored);

        // Assert
        outcome.Open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidDataKey");
        returned.Should().OnlyContain(b => b == 0, "the returned key is wiped even when refused");
    }

    [Fact]
    public async Task Rotation_DataKeyRewrapped_ReadsWithoutRewritingObject()
    {
        // Arrange
        var plaintext = PatternStream.Bytes(100_000);
        var stored = await _harness.PutAsync("doc.bin", plaintext);
        var before = _harness.Memory["doc.bin"];
        var dataKey = (await _harness.KeyProvider.UnwrapAsync(EncryptionHarness.TenantA, stored.KekId, stored.WrappedDek)).Value!;
        _harness.KeyProvider.Rotate(EncryptionHarness.TenantA);
        var rewrapped = (await _harness.KeyProvider.WrapAsync(EncryptionHarness.TenantA, dataKey)).Value!;

        // Act
        var outcome = await _harness.ReadAsync(stored.StorageKey, rewrapped.WrappedKey, rewrapped.KekId);

        // Assert
        rewrapped.KekId.Should().NotBe(stored.KekId);
        outcome.Succeeded.Should().BeTrue();
        outcome.Content.Should().Equal(plaintext);
        _harness.Memory["doc.bin"].Should().Equal(before);
    }

    [Fact]
    public async Task Put_AfterRotation_UsesNewKek()
    {
        // Arrange
        var first = await _harness.PutAsync("a.bin", [1]);
        var newKek = _harness.KeyProvider.Rotate(EncryptionHarness.TenantA);

        // Act
        var second = await _harness.PutAsync("b.bin", [2]);

        // Assert
        second.KekId.Should().Be(newKek).And.NotBe(first.KekId);
        (await _harness.ReadAsync(first)).Content.Should().Equal([1]);
        (await _harness.ReadAsync(second)).Content.Should().Equal([2]);
    }

    [Fact]
    public async Task Put_WipesDataKeyAfterUse()
    {
        // Act
        await _harness.PutAsync("doc.bin", [1, 2, 3]);

        // Assert
        _harness.KeyProvider.LastDataKeyCopy.Should().Contain(b => b != 0);
        _harness.KeyProvider.LastWrappedInput.ToArray().Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public async Task OpenRead_WipesUnwrappedDataKeyAfterUse()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);

        // Act
        var outcome = await _harness.ReadAsync(stored);

        // Assert
        outcome.Succeeded.Should().BeTrue();
        _harness.KeyProvider.LastUnwrapped.Should().HaveCount(32).And.OnlyContain(b => b == 0);
    }

    [Fact]
    public async Task Put_WrapFails_ReturnsErrorWithoutUploading()
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Error.Unavailable("Kms.Down", "KMS unavailable."));
        var harness = new EncryptionHarness(inner, keys);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new MemoryStream([1]));

        // Assert
        put.Error.Code.Should().Be("Kms.Down");
        await inner.DidNotReceiveWithAnyArgs().PutAsync(default!, default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Put_ProviderReturnsEmptyKekId_ReturnsInvalidKekId(string? kekId)
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Result.Success(new WrappedDataKey(kekId!, new byte[40])));
        var harness = new EncryptionHarness(inner, keys);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new MemoryStream([1]));

        // Assert
        put.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidKekId");
        await inner.DidNotReceiveWithAnyArgs().PutAsync(default!, default!);
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public async Task Put_KekIdLengthLimit_IsCountedInUtf8Bytes(int asciiLength, bool accepted)
    {
        // Arrange
        var kekId = new string('k', asciiLength);
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Result.Success(new WrappedDataKey(kekId, new byte[40])));
        var harness = new EncryptionHarness(keys: keys);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new MemoryStream([1]));

        // Assert
        put.IsSuccess.Should().Be(accepted);
    }

    [Fact]
    public async Task Put_MultibyteKekIdOverLimit_ReturnsInvalidKekId()
    {
        // Arrange — 128 characters, 256 UTF-8 bytes.
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(Result.Success(new WrappedDataKey(new string('é', 128), new byte[40])));
        var harness = new EncryptionHarness(keys: keys);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new MemoryStream([1]));

        // Assert
        put.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidKekId");
    }

    [Fact]
    public async Task PutAndOpenRead_MultibyteKekId_RoundTrips()
    {
        // Arrange
        var unwrapped = RandomNumberGenerator.GetBytes(32);
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.WrapAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            ((ReadOnlyMemory<byte>)call[1]).Span.CopyTo(unwrapped);
            return Result.Success(new WrappedDataKey("clé-ü", new byte[40]));
        });
        keys.UnwrapAsync(default!, default!, default).ReturnsForAnyArgs(_ => Result.Success(unwrapped.ToArray()));
        var harness = new EncryptionHarness(keys: keys);

        // Act
        var stored = await harness.PutAsync("doc.bin", [9, 8, 7]);
        var outcome = await harness.ReadAsync(stored);

        // Assert
        outcome.Content.Should().Equal([9, 8, 7]);
        harness.Memory["doc.bin"][5].Should().Be(7, "the length is in UTF-8 bytes");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task OpenRead_EmptyKekId_ReturnsInvalidKekIdWithoutFetching(string? kekId)
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        var harness = new EncryptionHarness(inner);

        // Act
        var open = await harness.Store.OpenReadAsync("doc.bin", new byte[40], kekId!);

        // Assert
        open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidKekId");
        await inner.DidNotReceiveWithAnyArgs().GetAsync(default!);
    }
}
