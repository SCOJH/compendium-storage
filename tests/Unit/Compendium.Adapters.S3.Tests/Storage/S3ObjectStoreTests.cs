// -----------------------------------------------------------------------
// <copyright file="S3ObjectStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.S3.Options;
using Compendium.Adapters.S3.Storage;
using Compendium.Core.Results;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;

namespace Compendium.Adapters.S3.Tests.Storage;

public class S3ObjectStoreTests
{
    private const string Bucket = "my-bucket";
    private const string TenantId = "tenant-a";

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();
    private readonly ITenantContextAccessor _tenantAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ILogger<S3ObjectStore> _logger = Substitute.For<ILogger<S3ObjectStore>>();
    private readonly S3Options _options = new() { Bucket = Bucket };

    public S3ObjectStoreTests()
    {
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns(TenantId);
        _tenantAccessor.TenantContext.Returns(_tenantContext);
    }

    private S3ObjectStore CreateSut()
    {
        return new S3ObjectStore(_s3, Microsoft.Extensions.Options.Options.Create(_options), _tenantAccessor, _logger);
    }

    private void StubHead(string storageKey, GetObjectMetadataResponse response)
    {
        _s3.GetObjectMetadataAsync(
                Arg.Is<GetObjectMetadataRequest>(r => r.BucketName == Bucket && r.Key == storageKey),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }

    // -------------------------- Constructor --------------------------

    [Fact]
    public void Constructor_NullS3_Throws()
    {
        // Arrange / Act
        var act = () => new S3ObjectStore(null!, Microsoft.Extensions.Options.Options.Create(_options), _tenantAccessor, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        // Arrange / Act
        var act = () => new S3ObjectStore(_s3, null!, _tenantAccessor, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTenantAccessor_Throws()
    {
        // Arrange / Act
        var act = () => new S3ObjectStore(_s3, Microsoft.Extensions.Options.Options.Create(_options), null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        // Arrange / Act
        var act = () => new S3ObjectStore(_s3, Microsoft.Extensions.Options.Options.Create(_options), _tenantAccessor, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------- Tenant resolution --------------------------

    [Fact]
    public async Task PutAsync_WithoutTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "application/octet-stream"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    [Fact]
    public async Task PutAsync_WithMalformedTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant with spaces");
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "application/octet-stream"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
        actual.Error.Code.Should().Be("S3.InvalidTenant");
    }

    // -------------------------- PutAsync (single-part) --------------------------

    [Fact]
    public async Task PutAsync_SmallPayload_UsesSinglePartUploadWithTenantPrefix()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        StubHead($"{TenantId}/invoices/inv-1.pdf", new GetObjectMetadataResponse
        {
            ContentLength = 5,
            ETag = "\"etag-1\"",
            LastModified = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc),
        });

        // Act
        var actual = await sut.PutAsync(
            "invoices/inv-1.pdf",
            ms,
            new ObjectMetadata(ContentType: "application/pdf"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Key.Should().Be("invoices/inv-1.pdf");
        actual.Value.Size.Should().Be(5);
        actual.Value.ETag.Should().Be("etag-1");
        actual.Value.LastModified.Should().Be(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r =>
                r.BucketName == Bucket
                && r.Key == $"{TenantId}/invoices/inv-1.pdf"
                && r.ContentType == "application/pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutAsync_NullMetadata_StillUploads()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        StubHead($"{TenantId}/k", new GetObjectMetadataResponse { ContentLength = 3, ETag = "\"e\"" });

        // Act
        var actual = await sut.PutAsync("k", ms);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_PropagatesCustomMetadata()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        StubHead($"{TenantId}/k", new GetObjectMetadataResponse { ContentLength = 3, ETag = "\"e\"" });

        var metadata = new ObjectMetadata(
            ContentType: "text/plain",
            CacheControl: "max-age=3600",
            ContentDisposition: "attachment; filename=\"k.txt\"",
            Custom: new Dictionary<string, string>
            {
                ["x-amz-meta-author"] = "alice",
                ["x-amz-meta-doc-id"] = "inv-001",
            });

        // Act
        var actual = await sut.PutAsync("k", ms, metadata);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.ContentType.Should().Be("text/plain");
        captured.Headers.CacheControl.Should().Be("max-age=3600");
        captured.Headers.ContentDisposition.Should().Be("attachment; filename=\"k.txt\"");
        captured.Metadata.Keys.Should().Contain(["x-amz-meta-author", "x-amz-meta-doc-id"]);
    }

    [Fact]
    public async Task PutAsync_WithSse_Aes256_SetsEncryptionHeader()
    {
        // Arrange
        _options.ServerSideEncryption = ServerSideEncryption.Aes256;
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        StubHead($"{TenantId}/k", new GetObjectMetadataResponse { ContentLength = 3, ETag = "\"e\"" });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ServerSideEncryptionMethod.Should().Be(ServerSideEncryptionMethod.AES256);
    }

    [Fact]
    public async Task PutAsync_WithSse_Kms_SetsKmsKeyId()
    {
        // Arrange
        _options.ServerSideEncryption = ServerSideEncryption.AwsKms;
        _options.KmsKeyId = "arn:aws:kms:us-east-1:123:key/abc";
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);
        PutObjectRequest? captured = null;
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        StubHead($"{TenantId}/k", new GetObjectMetadataResponse { ContentLength = 3, ETag = "\"e\"" });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ServerSideEncryptionMethod.Should().Be(ServerSideEncryptionMethod.AWSKMS);
        captured.ServerSideEncryptionKeyManagementServiceKeyId.Should().Be("arn:aws:kms:us-east-1:123:key/abc");
    }

    [Fact]
    public async Task PutAsync_AmazonS3Exception_MapsToFailureResult()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1]);
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("denied")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied",
            });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
        actual.Error.Code.Should().Be("Storage.AccessDenied");
    }

    [Fact]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PutAsync_InvalidKey_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1]);

        // Act
        var actual = await sut.PutAsync("../escape", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
        actual.Error.Code.Should().Be("S3.Put.InvalidKey");
    }

    [Fact]
    public async Task PutAsync_PostUploadHeadFails_ReturnsMappedError()
    {
        // Arrange
        var sut = CreateSut();
        using var ms = new MemoryStream([1, 2, 3]);
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("denied")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied",
            });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // -------------------------- PutAsync (multipart) --------------------------

    [Fact]
    public void ShouldUseMultipart_LargeSeekableStream_ReturnsTrue()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        using var ms = new MemoryStream(new byte[2048]);

        // Act
        var actual = sut.ShouldUseMultipart(ms);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void ShouldUseMultipart_SmallSeekableStream_ReturnsFalse()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        using var ms = new MemoryStream(new byte[100]);

        // Act
        var actual = sut.ShouldUseMultipart(ms);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void ShouldUseMultipart_NonSeekableStream_ReturnsFalse()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1;
        var sut = CreateSut();
        using var ms = new NonSeekableStream();

        // Act
        var actual = sut.ShouldUseMultipart(ms);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public async Task PutAsync_LargePayload_RoutesThroughMultipartUpload()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        // 12 MiB to force at least 2 parts at 5 MiB chunks.
        var payload = new byte[12 * 1024 * 1024];
        using var ms = new MemoryStream(payload);

        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });

        var partNumber = 0;
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                partNumber++;
                return new UploadPartResponse { ETag = $"etag-{partNumber}", PartNumber = partNumber };
            });

        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse());
        StubHead($"{TenantId}/big.bin", new GetObjectMetadataResponse
        {
            ContentLength = payload.Length,
            ETag = "\"multipart-etag\"",
        });

        // Act
        var actual = await sut.PutAsync(
            "big.bin",
            ms,
            new ObjectMetadata(ContentType: "application/octet-stream"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Size.Should().Be(payload.Length);
        await _s3.Received(1).InitiateMultipartUploadAsync(
            Arg.Is<InitiateMultipartUploadRequest>(r => r.Key == $"{TenantId}/big.bin"),
            Arg.Any<CancellationToken>());
        await _s3.Received(3).UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>());
        await _s3.Received(1).CompleteMultipartUploadAsync(
            Arg.Is<CompleteMultipartUploadRequest>(r =>
                r.UploadId == "upload-1" && r.PartETags.Count == 3),
            Arg.Any<CancellationToken>());
        await _s3.DidNotReceive().PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutAsync_MultipartPropagatesMetadata()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        var payload = new byte[6 * 1024 * 1024];
        using var ms = new MemoryStream(payload);

        InitiateMultipartUploadRequest? captured = null;
        _s3.InitiateMultipartUploadAsync(
                Arg.Do<InitiateMultipartUploadRequest>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });

        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UploadPartResponse { ETag = "etag-1", PartNumber = 1 });
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse());
        StubHead($"{TenantId}/big.bin", new GetObjectMetadataResponse
        {
            ContentLength = payload.Length,
            ETag = "\"multipart-etag\"",
        });

        var metadata = new ObjectMetadata(
            ContentType: "application/pdf",
            CacheControl: "no-cache",
            ContentDisposition: "inline",
            Custom: new Dictionary<string, string> { ["x-amz-meta-tag"] = "v" });

        // Act
        var actual = await sut.PutAsync("big.bin", ms, metadata);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.ContentType.Should().Be("application/pdf");
        captured.Headers.CacheControl.Should().Be("no-cache");
        captured.Headers.ContentDisposition.Should().Be("inline");
        captured.Metadata.Keys.Should().Contain("x-amz-meta-tag");
    }

    [Fact]
    public async Task PutAsync_MultipartWithNullMetadata_StillCompletes()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        var payload = new byte[6 * 1024 * 1024];
        using var ms = new MemoryStream(payload);

        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UploadPartResponse { ETag = "etag-1", PartNumber = 1 });
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse());
        StubHead($"{TenantId}/big.bin", new GetObjectMetadataResponse
        {
            ContentLength = payload.Length,
            ETag = "\"etag\"",
        });

        // Act
        var actual = await sut.PutAsync("big.bin", ms);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_MultipartWithSseKms_PropagatesKey()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        _options.ServerSideEncryption = ServerSideEncryption.AwsKms;
        _options.KmsKeyId = "arn:aws:kms:us-east-1:123:key/multipart";
        var sut = CreateSut();
        using var ms = new MemoryStream(new byte[6 * 1024 * 1024]);

        InitiateMultipartUploadRequest? captured = null;
        _s3.InitiateMultipartUploadAsync(
                Arg.Do<InitiateMultipartUploadRequest>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UploadPartResponse { ETag = "e", PartNumber = 1 });
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse());
        StubHead($"{TenantId}/k", new GetObjectMetadataResponse { ContentLength = 0, ETag = "\"e\"" });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ServerSideEncryptionMethod.Should().Be(ServerSideEncryptionMethod.AWSKMS);
        captured.ServerSideEncryptionKeyManagementServiceKeyId.Should().Be("arn:aws:kms:us-east-1:123:key/multipart");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WithSseAes256_AppliesEncryptionToRequest()
    {
        // Arrange
        _options.ServerSideEncryption = ServerSideEncryption.Aes256;
        var sut = CreateSut();
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
            .Returns("https://s3.example.com/k?sig=1");

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ServerSideEncryptionMethod.Should().Be(ServerSideEncryptionMethod.AES256);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WithSseKms_AppliesKey()
    {
        // Arrange
        _options.ServerSideEncryption = ServerSideEncryption.AwsKms;
        _options.KmsKeyId = "arn:aws:kms:us-east-1:123:key/presign";
        var sut = CreateSut();
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
            .Returns("https://s3.example.com/k?sig=2");

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ServerSideEncryptionMethod.Should().Be(ServerSideEncryptionMethod.AWSKMS);
        captured.ServerSideEncryptionKeyManagementServiceKeyId.Should().Be("arn:aws:kms:us-east-1:123:key/presign");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WithHttpServiceUrl_UsesHttpProtocol()
    {
        // Arrange
        _options.ServiceUrl = "http://localhost:9000";
        var sut = CreateSut();
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
            .Returns("http://localhost:9000/k?sig=3");

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.Protocol.Should().Be(Amazon.S3.Protocol.HTTP);
    }

    [Fact]
    public async Task PutAsync_MultipartUploadPartFailure_AbortsUpload()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        using var ms = new MemoryStream(new byte[6 * 1024 * 1024]);

        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });

        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("S3.Put.");
        await _s3.Received(1).AbortMultipartUploadAsync(
            Arg.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutAsync_MultipartAbortAlsoFails_StillReturnsOriginalError()
    {
        // Arrange
        _options.MultipartThresholdBytes = 1024;
        var sut = CreateSut();
        using var ms = new MemoryStream(new byte[6 * 1024 * 1024]);

        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-1" });

        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        _s3.AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("abort-failed") { StatusCode = HttpStatusCode.InternalServerError });

        // Act
        var actual = await sut.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("S3.Put.");
    }

    // -------------------------- GetAsync --------------------------

    [Fact]
    public async Task GetAsync_HappyPath_ReturnsObjectStream()
    {
        // Arrange
        var sut = CreateSut();
        var content = new MemoryStream([7, 7, 7]);
        var response = new GetObjectResponse
        {
            ResponseStream = content,
            ContentLength = 3,
            ETag = "\"etag-get\"",
            LastModified = new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc),
        };
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var actual = await sut.GetAsync("k.bin");

        // Assert
        actual.IsSuccess.Should().BeTrue();
        await _s3.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.BucketName == Bucket && r.Key == $"{TenantId}/k.bin"),
            Arg.Any<CancellationToken>());

        using var stream = actual.Value;
        stream.Content.Should().BeSameAs(content);
        stream.Info.Key.Should().Be("k.bin");
        stream.Info.Size.Should().Be(3);
        stream.Info.ETag.Should().Be("etag-get");
        stream.Info.LastModified.Should().Be(new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetAsync_Dispose_AlsoDisposesUnderlyingStream()
    {
        // Arrange
        var sut = CreateSut();
        var content = new TrackingStream();
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = content, ContentLength = 0 });

        // Act
        var actual = await sut.GetAsync("k");
        actual.Value.Dispose();

        // Assert
        content.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsStorageNotFound()
    {
        // Arrange
        var sut = CreateSut();
        _s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        // Act
        var actual = await sut.GetAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.NotFound);
        actual.Error.Code.Should().Be("Storage.NotFound");
    }

    [Fact]
    public async Task GetAsync_InvalidKey_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var actual = await sut.GetAsync("..");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.Get.InvalidKey");
    }

    [Fact]
    public async Task GetAsync_AlreadyCancelledToken_Throws()
    {
        // Arrange
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.GetAsync("k", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetAsync_NoTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();

        // Act
        var actual = await sut.GetAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    // -------------------------- DeleteAsync --------------------------

    [Fact]
    public async Task DeleteAsync_HappyPath()
    {
        // Arrange
        var sut = CreateSut();
        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());

        // Act
        var actual = await sut.DeleteAsync("k");

        // Assert
        actual.IsSuccess.Should().BeTrue();
        await _s3.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(r => r.BucketName == Bucket && r.Key == $"{TenantId}/k"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_AmazonS3Exception_Maps()
    {
        // Arrange
        var sut = CreateSut();
        _s3.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        // Act
        var actual = await sut.DeleteAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("S3.Delete.");
    }

    [Fact]
    public async Task DeleteAsync_InvalidKey_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var actual = await sut.DeleteAsync("");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task DeleteAsync_NoTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();

        // Act
        var actual = await sut.DeleteAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    [Fact]
    public async Task DeleteAsync_AlreadyCancelledToken_Throws()
    {
        // Arrange
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.DeleteAsync("k", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------- ExistsAsync --------------------------

    [Fact]
    public async Task ExistsAsync_WhenObjectPresent_ReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse());

        // Act
        var actual = await sut.ExistsAsync("k");

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().BeTrue();
        await _s3.Received(1).GetObjectMetadataAsync(
            Arg.Is<GetObjectMetadataRequest>(r => r.Key == $"{TenantId}/k"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectMissing_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("nope") { StatusCode = HttpStatusCode.NotFound });

        // Act
        var actual = await sut.ExistsAsync("k");

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_OtherException_ReturnsError()
    {
        // Arrange
        var sut = CreateSut();
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        // Act
        var actual = await sut.ExistsAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("S3.Exists.");
    }

    [Fact]
    public async Task ExistsAsync_InvalidKey_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var actual = await sut.ExistsAsync("");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.Exists.InvalidKey");
    }

    [Fact]
    public async Task ExistsAsync_NoTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();

        // Act
        var actual = await sut.ExistsAsync("k");

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    // -------------------------- ListAsync --------------------------

    [Fact]
    public async Task ListAsync_NoTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();

        // Act
        var actual = await sut.ListAsync();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    [Fact]
    public async Task ListAsync_DefaultOptions_ScopesPrefixToTenantAndUsesDefaultMaxKeys()
    {
        // Arrange
        var sut = CreateSut();
        ListObjectsV2Request? captured = null;
        _s3.ListObjectsV2Async(
                Arg.Do<ListObjectsV2Request>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false });

        // Act
        var actual = await sut.ListAsync();

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Prefix.Should().Be($"{TenantId}/");
        captured.MaxKeys.Should().Be(1000);
        captured.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_StripsTenantPrefixOnResults()
    {
        // Arrange
        var sut = CreateSut();
        var response = new ListObjectsV2Response
        {
            IsTruncated = false,
            S3Objects =
            [
                new S3Object
                {
                    Key = $"{TenantId}/a.txt",
                    Size = 100,
                    ETag = "\"etag-a\"",
                    LastModified = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc),
                },
                new S3Object
                {
                    Key = $"{TenantId}/dir/b.txt",
                    Size = 200,
                    ETag = "\"etag-b\"",
                    LastModified = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc),
                },
            ],
        };
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var actual = await sut.ListAsync();

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Items.Should().HaveCount(2);
        actual.Value.Items[0].Key.Should().Be("a.txt");
        actual.Value.Items[0].Size.Should().Be(100);
        actual.Value.Items[0].ETag.Should().Be("etag-a");
        actual.Value.Items[0].LastModified.Should().Be(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        actual.Value.Items[1].Key.Should().Be("dir/b.txt");
        actual.Value.NextContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_PrefixOption_ComposesTenantPrefix()
    {
        // Arrange
        var sut = CreateSut();
        ListObjectsV2Request? captured = null;
        _s3.ListObjectsV2Async(
                Arg.Do<ListObjectsV2Request>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false });

        // Act
        var actual = await sut.ListAsync(new ListOptions(Prefix: "invoices/"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.Prefix.Should().Be($"{TenantId}/invoices/");
    }

    [Fact]
    public async Task ListAsync_PageSizeOption_PassedAsMaxKeys()
    {
        // Arrange
        var sut = CreateSut();
        ListObjectsV2Request? captured = null;
        _s3.ListObjectsV2Async(
                Arg.Do<ListObjectsV2Request>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false });

        // Act
        var actual = await sut.ListAsync(new ListOptions(MaxKeys: 50));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.MaxKeys.Should().Be(50);
    }

    [Fact]
    public async Task ListAsync_TruncatedResponse_PropagatesContinuationToken()
    {
        // Arrange
        var sut = CreateSut();
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "next-token",
                S3Objects =
                [
                    new S3Object { Key = $"{TenantId}/a", Size = 1, ETag = "\"e\"" },
                ],
            });

        // Act
        var actual = await sut.ListAsync();

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.NextContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListAsync_ContinuationToken_PassedToS3()
    {
        // Arrange
        var sut = CreateSut();
        ListObjectsV2Request? captured = null;
        _s3.ListObjectsV2Async(
                Arg.Do<ListObjectsV2Request>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { IsTruncated = false });

        // Act
        var actual = await sut.ListAsync(new ListOptions(ContinuationToken: "tok"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.ContinuationToken.Should().Be("tok");
    }

    [Fact]
    public async Task ListAsync_MultiPageRoundTrip_HonorsContinuation()
    {
        // Arrange
        var sut = CreateSut();
        _s3.ListObjectsV2Async(
                Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == null),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "tok",
                S3Objects =
                [
                    new S3Object { Key = $"{TenantId}/a", Size = 1, ETag = "\"e\"" },
                ],
            });
        _s3.ListObjectsV2Async(
                Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == "tok"),
                Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                IsTruncated = false,
                S3Objects =
                [
                    new S3Object { Key = $"{TenantId}/b", Size = 2, ETag = "\"e2\"" },
                ],
            });

        // Act : first page.
        var page1 = await sut.ListAsync();

        // Assert
        page1.IsSuccess.Should().BeTrue();
        page1.Value.Items.Should().HaveCount(1);
        page1.Value.Items[0].Key.Should().Be("a");
        page1.Value.NextContinuationToken.Should().Be("tok");

        // Act : second page.
        var page2 = await sut.ListAsync(new ListOptions(ContinuationToken: page1.Value.NextContinuationToken));

        // Assert
        page2.IsSuccess.Should().BeTrue();
        page2.Value.Items.Should().HaveCount(1);
        page2.Value.Items[0].Key.Should().Be("b");
        page2.Value.NextContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_AmazonS3Exception_ReturnsFailure()
    {
        // Arrange
        var sut = CreateSut();
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        // Act
        var actual = await sut.ListAsync();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("S3.List.");
    }

    [Fact]
    public async Task ListAsync_AlreadyCancelledToken_Throws()
    {
        // Arrange
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.ListAsync(cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------- GetPresignedUrlAsync --------------------------

    [Fact]
    public async Task GetPresignedUrlAsync_Get_UsesHttpVerbGet()
    {
        // Arrange
        var sut = CreateSut();
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
            .Returns("https://s3.example.com/my-bucket/tenant-a/k?sig=abc");

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(5));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.AbsoluteUri.Should().Be("https://s3.example.com/my-bucket/tenant-a/k?sig=abc");
        captured.Should().NotBeNull();
        captured!.Verb.Should().Be(HttpVerb.GET);
        captured.Key.Should().Be($"{TenantId}/k");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Put_UsesHttpVerbPut()
    {
        // Arrange
        var sut = CreateSut();
        GetPreSignedUrlRequest? captured = null;
        _s3.GetPreSignedURLAsync(Arg.Do<GetPreSignedUrlRequest>(r => captured = r))
            .Returns("https://s3.example.com/?sig=put");

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Put, TimeSpan.FromMinutes(5));

        // Assert
        actual.IsSuccess.Should().BeTrue();
        captured!.Verb.Should().Be(HttpVerb.PUT);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_NonPositiveExpiry_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.Zero);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_InvalidKey_ReturnsValidationError()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var actual = await sut.GetPresignedUrlAsync("..", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.Presign.InvalidKey");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_AmazonS3Exception_Maps()
    {
        // Arrange
        var sut = CreateSut();
        _s3.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>())
            .Throws(new AmazonS3Exception("nope") { StatusCode = HttpStatusCode.Forbidden });

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_NoTenant_ReturnsForbidden()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);
        var sut = CreateSut();

        // Act
        var actual = await sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("S3.NoTenant");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_AlreadyCancelledToken_Throws()
    {
        // Arrange
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Helper non-seekable stream for multipart routing tests.
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
