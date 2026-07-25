// -----------------------------------------------------------------------
// <copyright file="MinioFixture.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Amazon.S3;
using Amazon.S3.Model;
using Compendium.Adapters.S3.DependencyInjection;
using Compendium.Adapters.S3.Options;
using Testcontainers.Minio;
using Xunit;

namespace Compendium.Adapters.S3.IntegrationTests.Fixtures;

/// <summary>
/// Boots a single MinIO container shared by the test class via
/// <see cref="IClassFixture{T}"/> or used per-class via <see cref="IAsyncLifetime"/>.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private readonly MinioContainer _container = new MinioBuilder("minio/minio:latest")
        .WithUsername("minio-test")
        .WithPassword("minio-test-password")
        .Build();

    /// <summary>S3 endpoint URL exposed by the container.</summary>
    public string ServiceUrl { get; private set; } = string.Empty;

    /// <summary>Bucket created during fixture initialisation.</summary>
    public string Bucket { get; } = $"compendium-s3-{Guid.NewGuid():N}";

    /// <summary>Access key for the embedded MinIO root user.</summary>
    public string AccessKey => "minio-test";

    /// <summary>Secret key for the embedded MinIO root user.</summary>
    public string SecretKey => "minio-test-password";

    /// <summary>S3 client connected to the container.</summary>
    public IAmazonS3 Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        ServiceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9000)}";

        Client = ServiceCollectionExtensions.CreateClient(BuildOptions());

        await Client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket })
            .ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds an <see cref="S3Options"/> configured for this container.
    /// </summary>
    /// <returns>Options bound to the running MinIO instance.</returns>
    public S3Options BuildOptions()
    {
        return new S3Options
        {
            Bucket = Bucket,
            ServiceUrl = ServiceUrl,
            ForcePathStyle = true,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Region = "us-east-1",
        };
    }
}
