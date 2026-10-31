// -----------------------------------------------------------------------
// <copyright file="NonSeekableUploadIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.S3.IntegrationTests.Fixtures;
using Compendium.Adapters.S3.IntegrationTests.TestSupport;
using Compendium.Adapters.S3.Storage;
using Compendium.Storage.Encryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.S3.IntegrationTests.Storage;

/// <summary>
/// Runs alone: retained memory is measured process-wide, so no other test may hold buffers meanwhile.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BoundedMemoryCollection
{
    public const string Name = "Bounded memory";
}

/// <summary>
/// Uploads of non-seekable streams (whose length is unknown up front, such as the output of
/// <see cref="EncryptingObjectStore"/>) against a real MinIO container: they must go multipart in 5 MiB parts
/// and keep retained memory bounded, whatever the payload size.
/// </summary>
[Collection(BoundedMemoryCollection.Name)]
public sealed class NonSeekableUploadIntegrationTests : IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private const long MiB = 1024 * 1024;

    // One 5 MiB part buffer plus SDK and HTTP buffers. Holding the payload would mean 100 MiB or more.
    private const long RetainedBudget = 32 * MiB;

    private readonly MinioFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [RequiresDockerFact]
    public async Task PutAsync_NonSeekable100MiB_UploadsInPartsWithBoundedMemory()
    {
        // Arrange
        var sut = CreateStore();

        // Act
        var probe = LiveMemoryProbe.Start();
        var put = await sut.PutAsync("big.bin", new PatternStream(100 * MiB));
        var retained = await probe.StopAsync();

        // Assert
        put.IsSuccess.Should().BeTrue(put.IsFailure ? put.Error.Message : null);
        put.Value.Size.Should().Be(100 * MiB);
        put.Value.ETag.Should().EndWith("-20", "100 MiB in 5 MiB parts");
        retained.Should().BeLessThan(RetainedBudget);

        var get = await sut.GetAsync("big.bin");
        await using var stream = get.Value;
        (await PatternStream.VerifyAsync(stream.Content)).Should().Be(100 * MiB);
    }

    [RequiresDockerFact]
    public async Task EncryptedPutAndRead_100MiB_StreamsThroughS3WithBoundedMemory()
    {
        // Arrange
        var store = new EncryptingObjectStore(
            CreateStore(),
            new InMemoryKeyProvider(),
            new StaticTenantContextAccessor(Tenant));

        // Act — upload.
        var uploadProbe = LiveMemoryProbe.Start();
        var put = await store.PutAsync("files/big.enc", new PatternStream(100 * MiB));
        var uploadRetained = await uploadProbe.StopAsync();
        put.IsSuccess.Should().BeTrue(put.IsFailure ? put.Error.Message : null);

        // Act — download, decrypt and verify every byte.
        var downloadProbe = LiveMemoryProbe.Start();
        var open = await store.OpenReadAsync(put.Value!.StorageKey, put.Value.WrappedDek, put.Value.KekId);
        open.IsSuccess.Should().BeTrue(open.IsFailure ? open.Error.Message : null);
        long verified;
        await using (var plaintext = open.Value!)
        {
            verified = await PatternStream.VerifyAsync(plaintext.Content);
        }

        var downloadRetained = await downloadProbe.StopAsync();

        // Assert
        put.Value!.PlaintextLength.Should().Be(100 * MiB);
        put.Value.Info.Size.Should().Be(EncryptedLength(100 * MiB, put.Value.KekId));
        put.Value.Info.ETag.Should().EndWith("-21", "100 MiB plus tags and header, in 5 MiB parts");
        verified.Should().Be(100 * MiB);
        uploadRetained.Should().BeLessThan(RetainedBudget);
        downloadRetained.Should().BeLessThan(RetainedBudget);
    }

    [RequiresDockerFact]
    public async Task PutAsync_NonSeekableAroundPartSize_RoundTrips()
    {
        // Arrange
        var sut = CreateStore();
        var cases = new (long Length, string? PartsSuffix)[]
        {
            (0, null),
            (1024, null),
            ((5 * MiB) - 1, null),
            (5 * MiB, "-1"),
            ((5 * MiB) + 1, "-2"),
        };

        foreach (var (length, suffix) in cases)
        {
            var key = $"sizes/{length}.bin";

            // Act
            var put = await sut.PutAsync(key, new PatternStream(length));

            // Assert
            put.IsSuccess.Should().BeTrue(put.IsFailure ? put.Error.Message : null);
            put.Value.Size.Should().Be(length);
            if (suffix is null)
            {
                put.Value.ETag.Should().NotContain("-", $"{length} bytes fit a single PUT");
            }
            else
            {
                put.Value.ETag.Should().EndWith(suffix);
            }

            var get = await sut.GetAsync(key);
            await using var stream = get.Value;
            (await PatternStream.VerifyAsync(stream.Content)).Should().Be(length);
        }
    }

    private static long EncryptedLength(long plaintext, string kekId)
    {
        const long block = 64 * 1024;
        var blocks = Math.Max(1, (plaintext + block - 1) / block);
        return 6 + kekId.Length + 7 + plaintext + (16 * blocks);
    }

    private S3ObjectStore CreateStore() => new(
        _fixture.Client,
        Microsoft.Extensions.Options.Options.Create(_fixture.BuildOptions()),
        new StaticTenantContextAccessor(Tenant),
        NullLogger<S3ObjectStore>.Instance);
}
