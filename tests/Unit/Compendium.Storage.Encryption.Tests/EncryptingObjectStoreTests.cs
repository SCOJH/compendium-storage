// -----------------------------------------------------------------------
// <copyright file="EncryptingObjectStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Abstractions.Storage.Models;
using Compendium.Multitenancy;
using Compendium.Storage.Encryption.Tests.TestSupport;

namespace Compendium.Storage.Encryption.Tests;

public sealed class EncryptingObjectStoreTests
{
    private readonly EncryptionHarness _harness = new();

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        var accessor = Substitute.For<ITenantContextAccessor>();

        // Act
        var noInner = () => new EncryptingObjectStore(null!, keys, accessor);
        var noKeys = () => new EncryptingObjectStore(inner, null!, accessor);
        var noTenant = () => new EncryptingObjectStore(inner, keys, null!);

        // Assert
        noInner.Should().Throw<ArgumentNullException>();
        noKeys.Should().Throw<ArgumentNullException>();
        noTenant.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PutAsync_NullContent_Throws()
    {
        // Act
        var act = () => _harness.Store.PutAsync("doc.bin", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PutAndOpenRead_NoTenantInScope_ReturnNoTenant(string? tenantId)
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1]);
        _harness.SetTenant(tenantId);

        // Act
        var put = await _harness.Store.PutAsync("other.bin", new MemoryStream([1]));
        var open = await _harness.Store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);

        // Assert
        put.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".NoTenant");
        open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".NoTenant");
    }

    [Fact]
    public async Task PutAndOpenRead_NullTenantContext_ReturnNoTenant()
    {
        // Arrange
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.TenantContext.Returns((ITenantContext?)null);
        var store = new EncryptingObjectStore(new InMemoryObjectStore(), new InMemoryKeyProvider(), accessor);

        // Act
        var put = await store.PutAsync("doc.bin", new MemoryStream([1]));
        var open = await store.OpenReadAsync("doc.bin", new byte[40], "kek-1");

        // Assert
        put.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".NoTenant");
        open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".NoTenant");
    }

    [Fact]
    public async Task PutAsync_InnerStoreFails_ReturnsItsError()
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        inner.PutAsync(default!, default!).ReturnsForAnyArgs(StorageErrors.AccessDenied("doc.bin"));
        var harness = new EncryptionHarness(inner);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new MemoryStream([1]));

        // Assert
        put.Error.Code.Should().Be("Storage.AccessDenied");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(70_000)]
    public async Task PutAsync_InnerStoreStopsReadingEarly_ReturnsIncompleteWriteAndDeletes(int consumed)
    {
        // Arrange
        var inner = Substitute.For<IObjectStore>();
        inner.PutAsync(default!, default!).ReturnsForAnyArgs(async call =>
        {
            var content = (Stream)call[1];
            var buffer = new byte[consumed];
            await content.ReadAtLeastAsync(buffer, consumed, throwOnEndOfStream: false);
            return Result.Success(new ObjectInfo("doc.bin", consumed, "etag", null, DateTimeOffset.UnixEpoch));
        });
        inner.DeleteAsync(default!).ReturnsForAnyArgs(Result.Success());
        var harness = new EncryptionHarness(inner);

        // Act
        var put = await harness.Store.PutAsync("doc.bin", new PatternStream(200_000));

        // Assert
        put.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IncompleteWrite");
        await inner.Received(1).DeleteAsync("doc.bin", CancellationToken.None);
    }

    [Fact]
    public async Task OpenReadAsync_InnerStoreFails_ReturnsItsError()
    {
        // Act
        var open = await _harness.Store.OpenReadAsync("missing.bin", new byte[40], "kek-1");

        // Assert
        open.Error.Code.Should().Be("Storage.NotFound");
    }

    [Fact]
    public async Task OpenReadAsync_ProviderThrows_DisposesStoredStreamAndRethrows()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1]);
        var content = new TrackingStream(_harness.Memory["doc.bin"]);
        var keys = Substitute.For<IKeyEncryptionKeyProvider>();
        keys.UnwrapAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException<Result<byte[]>>(new InvalidOperationException("boom")));
        var store = new EncryptingObjectStore(StoreServing(content), keys, AccessorFor(EncryptionHarness.TenantA));

        // Act
        var act = () => store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        content.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsync_FailureAfterFetch_DisposesStoredStream()
    {
        // Arrange
        var content = new TrackingStream(PatternStream.Bytes(100));
        var store = new EncryptingObjectStore(StoreServing(content), new InMemoryKeyProvider(), AccessorFor(EncryptionHarness.TenantA));

        // Act
        var open = await store.OpenReadAsync("doc.bin", new byte[40], "kek-1");

        // Assert
        open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".InvalidHeader");
        content.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsync_IntegrityFailure_DisposesStoredStream()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        var raw = _harness.Memory["doc.bin"];
        raw[^1] ^= 1;
        var content = new TrackingStream(raw);
        var store = new EncryptingObjectStore(StoreServing(content), _harness.KeyProvider, AccessorFor(EncryptionHarness.TenantA));

        // Act
        var open = await store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);

        // Assert
        open.Error.Code.Should().Be(StorageEncryptionErrors.Prefix + ".IntegrityCheckFailed");
        content.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenReadAsync_ReadCancelledInHeaderOrFirstBlock_DisposesAndRethrows(bool inFirstBlock)
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        var content = new TrackingStream(_harness.Memory["doc.bin"])
        {
            CancelFrom = inFirstBlock ? EncryptionHarness.HeaderLength(stored.KekId) : 1,
        };
        var store = new EncryptingObjectStore(StoreServing(content), _harness.KeyProvider, AccessorFor(EncryptionHarness.TenantA));

        // Act
        var act = () => store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        content.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsync_DisposingResult_DisposesStoredStream()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1, 2, 3]);
        var content = new TrackingStream(_harness.Memory["doc.bin"]);
        var store = new EncryptingObjectStore(StoreServing(content), _harness.KeyProvider, AccessorFor(EncryptionHarness.TenantA));
        var open = await store.OpenReadAsync(stored.StorageKey, stored.WrappedDek, stored.KekId);

        // Act
        open.Value!.Dispose();

        // Assert
        content.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Methods_CancelledToken_Throw()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var put = () => _harness.Store.PutAsync("doc.bin", new MemoryStream([1]), cancellationToken: cts.Token);
        var open = () => _harness.Store.OpenReadAsync("doc.bin", new byte[40], "kek-1", cts.Token);

        // Assert
        await put.Should().ThrowAsync<OperationCanceledException>();
        await open.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInnerStore()
    {
        // Arrange
        var stored = await _harness.PutAsync("doc.bin", [1]);

        // Act
        var deleted = await _harness.Store.DeleteAsync(stored.StorageKey);

        // Assert
        deleted.IsSuccess.Should().BeTrue();
        (await _harness.Memory.ExistsAsync("doc.bin")).Value.Should().BeFalse();
    }

    private static IObjectStore StoreServing(Stream content)
    {
        var inner = Substitute.For<IObjectStore>();
        inner.GetAsync(default!).ReturnsForAnyArgs(Result.Success(new ObjectStream(
            content,
            new ObjectInfo("doc.bin", 100, "etag", null, DateTimeOffset.UnixEpoch))));
        return inner;
    }

    private static ITenantContextAccessor AccessorFor(string tenantId)
    {
        var context = Substitute.For<ITenantContext>();
        context.HasTenant.Returns(true);
        context.TenantId.Returns(tenantId);
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.TenantContext.Returns(context);
        return accessor;
    }

    private sealed class TrackingStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public bool Disposed { get; private set; }

        public long CancelFrom { get; init; } = long.MaxValue;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_inner.Position >= CancelFrom)
            {
                throw new OperationCanceledException();
            }

            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
