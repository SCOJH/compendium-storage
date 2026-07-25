// -----------------------------------------------------------------------
// <copyright file="S3ObjectStoreIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Text;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.S3.IntegrationTests.Fixtures;
using Compendium.Adapters.S3.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Compendium.Adapters.S3.IntegrationTests.Storage;

/// <summary>
/// End-to-end roundtrip tests against a real MinIO container.
/// Skipped automatically when Docker is unavailable.
/// </summary>
public sealed class S3ObjectStoreIntegrationTests : IAsyncLifetime
{
    private readonly MinioFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private S3ObjectStore CreateSut(string tenantId)
    {
        return new S3ObjectStore(
            _fixture.Client,
            Microsoft.Extensions.Options.Options.Create(_fixture.BuildOptions()),
            new StaticTenantContextAccessor(tenantId),
            NullLogger<S3ObjectStore>.Instance);
    }

    [RequiresDockerFact]
    public async Task PutGetExistsListDelete_Roundtrip_SmallPayload()
    {
        // Arrange
        var sut = CreateSut("tenant-a");
        const string key = "invoices/2026/inv-001.txt";
        var payload = Encoding.UTF8.GetBytes("hello compendium");

        // Act + Assert : put.
        using (var ms = new MemoryStream(payload))
        {
            var put = await sut.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"));
            put.IsSuccess.Should().BeTrue();
            put.Value.Key.Should().Be(key);
            put.Value.Size.Should().Be(payload.Length);
            put.Value.ETag.Should().NotBeNullOrEmpty();
        }

        // Act + Assert : exists.
        var exists = await sut.ExistsAsync(key);
        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeTrue();

        // Act + Assert : get.
        var get = await sut.GetAsync(key);
        get.IsSuccess.Should().BeTrue();
        using (var stream = get.Value)
        using (var read = new MemoryStream())
        {
            stream.Info.Key.Should().Be(key);
            await stream.Content.CopyToAsync(read);
            read.ToArray().Should().Equal(payload);
        }

        // Act + Assert : list.
        var list = await sut.ListAsync(new ListOptions(Prefix: "invoices/"));
        list.IsSuccess.Should().BeTrue();
        list.Value.Items.Should().ContainSingle(o => o.Key == key);

        // Act + Assert : delete.
        var del = await sut.DeleteAsync(key);
        del.IsSuccess.Should().BeTrue();
        (await sut.ExistsAsync(key)).Value.Should().BeFalse();
    }

    [RequiresDockerFact]
    public async Task PutLargePayload_RoutesMultipart_AndRoundtrips()
    {
        // Arrange
        var opt = _fixture.BuildOptions();
        opt.MultipartThresholdBytes = 1024 * 1024; // 1 MiB threshold

        var sut = new S3ObjectStore(
            _fixture.Client,
            Microsoft.Extensions.Options.Options.Create(opt),
            new StaticTenantContextAccessor("tenant-a"),
            NullLogger<S3ObjectStore>.Instance);

        var payload = new byte[12 * 1024 * 1024];
        new Random(42).NextBytes(payload);

        // Act + Assert : put (multipart).
        using (var ms = new MemoryStream(payload))
        {
            var put = await sut.PutAsync("big.bin", ms, new ObjectMetadata(ContentType: "application/octet-stream"));
            put.IsSuccess.Should().BeTrue();
            put.Value.Size.Should().Be(payload.Length);
        }

        // Act + Assert : roundtrip the bytes.
        var get = await sut.GetAsync("big.bin");
        get.IsSuccess.Should().BeTrue();
        using var stream = get.Value;
        using var read = new MemoryStream();
        await stream.Content.CopyToAsync(read);
        read.ToArray().Should().Equal(payload);
    }

    [RequiresDockerFact]
    public async Task TenantIsolation_TenantB_CannotSeeTenantAObjects()
    {
        // Arrange
        var aSut = CreateSut("tenant-a");
        var bSut = CreateSut("tenant-b");
        const string key = "private.txt";

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("secret")))
        {
            (await aSut.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        // Act + Assert : tenant B's exists returns false.
        var exists = await bSut.ExistsAsync(key);
        exists.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeFalse();

        // Act + Assert : tenant B's list comes back empty.
        var bList = await bSut.ListAsync();
        bList.IsSuccess.Should().BeTrue();
        bList.Value.Items.Should().BeEmpty();

        // Act + Assert : tenant B's get fails with NotFound.
        var get = await bSut.GetAsync(key);
        get.IsFailure.Should().BeTrue();
        get.Error.Type.Should().Be(Compendium.Core.Results.ErrorType.NotFound);
    }

    [RequiresDockerFact]
    public async Task ListAsync_Pagination_HonorsContinuationToken()
    {
        // Arrange
        var sut = CreateSut("tenant-a");
        const int total = 5;
        for (var i = 0; i < total; i++)
        {
            using var ms = new MemoryStream([(byte)i]);
            (await sut.PutAsync($"paged/{i}.bin", ms, new ObjectMetadata(ContentType: "application/octet-stream")))
                .IsSuccess.Should().BeTrue();
        }

        // Act : first page (size 2).
        var page1 = await sut.ListAsync(new ListOptions(Prefix: "paged/", MaxKeys: 2));

        // Assert
        page1.IsSuccess.Should().BeTrue();
        page1.Value.Items.Should().HaveCount(2);
        page1.Value.NextContinuationToken.Should().NotBeNullOrEmpty();

        // Act : drain remaining pages.
        var seen = new List<string>(page1.Value.Items.Select(o => o.Key));
        var token = page1.Value.NextContinuationToken;
        while (!string.IsNullOrEmpty(token))
        {
            var next = await sut.ListAsync(new ListOptions(Prefix: "paged/", MaxKeys: 2, ContinuationToken: token));
            next.IsSuccess.Should().BeTrue();
            seen.AddRange(next.Value.Items.Select(o => o.Key));
            token = next.Value.NextContinuationToken;
        }

        // Assert : every uploaded object surfaced exactly once.
        seen.Should().HaveCount(total);
        seen.Distinct().Should().HaveCount(total);
    }

    [RequiresDockerFact]
    public async Task PresignedGet_DownloadsObject()
    {
        // Arrange
        var sut = CreateSut("tenant-a");
        const string key = "presign/sample.txt";
        var payload = Encoding.UTF8.GetBytes("presigned payload");

        using (var ms = new MemoryStream(payload))
        {
            (await sut.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        // Act : generate URL.
        var presigned = await sut.GetPresignedUrlAsync(key, PresignedAction.Get, TimeSpan.FromMinutes(5));

        // Assert : presign succeeded.
        presigned.IsSuccess.Should().BeTrue();
        presigned.Value.Should().NotBeNull();

        // Act : hit it with a raw HttpClient.
        using var http = new HttpClient();
        using var response = await http.GetAsync(presigned.Value);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert : payload roundtrips.
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(payload);
    }
}
