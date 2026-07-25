// -----------------------------------------------------------------------
// <copyright file="S3ObjectStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Amazon.S3;
using Amazon.S3.Model;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.S3.Options;
using Compendium.Adapters.S3.Security;
using Compendium.Core.Results;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.S3.Storage;

/// <summary>
/// S3-compatible <see cref="IObjectStore"/> implementation, bound to the published
/// <c>Compendium.Abstractions.Storage</c> port.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the tenant id from <see cref="ITenantContextAccessor"/> at every call,
/// prepends <c>{tenantId}/</c> to the key, and delegates to <see cref="IAmazonS3"/>.
/// </para>
/// <para>
/// Multipart upload is automatic above <see cref="S3Options.MultipartThresholdBytes"/>.
/// SSE-S3 (AES-256) and SSE-KMS encryption are propagated to every <c>PUT</c>.
/// </para>
/// </remarks>
public sealed class S3ObjectStore : IObjectStore
{
    private const int MultipartChunkBytes = 5 * 1024 * 1024; // 5 MiB — S3 minimum.

    private readonly IAmazonS3 _s3;
    private readonly S3Options _options;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<S3ObjectStore> _logger;

    /// <summary>
    /// Initialises a new <see cref="S3ObjectStore"/>.
    /// </summary>
    /// <param name="s3">AWS SDK client.</param>
    /// <param name="options">Adapter configuration.</param>
    /// <param name="tenantAccessor">Tenant context accessor (resolved from DI).</param>
    /// <param name="logger">Diagnostic logger.</param>
    public S3ObjectStore(
        IAmazonS3 s3,
        IOptions<S3Options> options,
        ITenantContextAccessor tenantAccessor,
        ILogger<S3ObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(s3);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _s3 = s3;
        _options = options.Value;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ObjectInfo>> PutAsync(
        string key,
        Stream content,
        ObjectMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        string storageKey;
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("S3.Put.InvalidKey", ex.Message);
        }

        try
        {
            if (ShouldUseMultipart(content))
            {
                await PutMultipartAsync(storageKey, content, metadata, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await PutSinglePartAsync(storageKey, content, metadata, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await DescribeAsync(tenantId, storageKey, key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 PUT failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Put", key);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ObjectStream>> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        string storageKey;
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("S3.Get.InvalidKey", ex.Message);
        }

        try
        {
            var response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _options.Bucket, Key = storageKey },
                cancellationToken).ConfigureAwait(false);

            var info = new ObjectInfo(
                Key: key,
                Size: response.ContentLength,
                ETag: NormalizeETag(response.ETag),
                ContentType: response.Headers?.ContentType,
                LastModified: NormalizeLastModified(response.LastModified),
                Metadata: ExtractUserMetadata(response.Metadata));

            return new ObjectStream(response.ResponseStream, info);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 GET failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Get", key);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        string storageKey;
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("S3.Delete.InvalidKey", ex.Message);
        }

        try
        {
            await _s3.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _options.Bucket, Key = storageKey },
                cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 DELETE failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Delete", key);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        string storageKey;
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("S3.Exists.InvalidKey", ex.Message);
        }

        try
        {
            await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _options.Bucket, Key = storageKey },
                cancellationToken).ConfigureAwait(false);
            return Result.Success(true);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Success(false);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 HEAD failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Exists", key);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ListPage>> ListAsync(
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        var tenantPrefix = $"{tenantId}/";
        var subPrefix = options?.Prefix;
        var fullPrefix = string.IsNullOrEmpty(subPrefix)
            ? tenantPrefix
            : $"{tenantPrefix}{subPrefix}";

        var request = new ListObjectsV2Request
        {
            BucketName = _options.Bucket,
            Prefix = fullPrefix,
            MaxKeys = options?.MaxKeys ?? 1000,
            ContinuationToken = options?.ContinuationToken,
        };

        try
        {
            var response = await _s3.ListObjectsV2Async(request, cancellationToken)
                .ConfigureAwait(false);

            var items = new List<ObjectInfo>();
            if (response.S3Objects is not null)
            {
                foreach (var obj in response.S3Objects)
                {
                    items.Add(new ObjectInfo(
                        Key: TenantKey.Strip(tenantId, obj.Key),
                        Size: obj.Size ?? 0L,
                        ETag: NormalizeETag(obj.ETag),
                        ContentType: null,
                        LastModified: NormalizeLastModified(obj.LastModified)));
                }
            }

            var nextToken = response.IsTruncated == true
                ? response.NextContinuationToken
                : null;

            return new ListPage(items, nextToken);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 LIST failed for prefix {Prefix}", fullPrefix);
            return S3ErrorMapping.Map(ex, "List", subPrefix ?? string.Empty);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Uri>> GetPresignedUrlAsync(
        string key,
        PresignedAction action,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var error))
        {
            return error;
        }

        string storageKey;
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("S3.Presign.InvalidKey", ex.Message);
        }

        if (expiresIn <= TimeSpan.Zero)
        {
            return Error.Validation("S3.Presign.InvalidExpiry", "Expiry must be positive.");
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = storageKey,
            Expires = DateTime.UtcNow.Add(expiresIn),
            Verb = action switch
            {
                PresignedAction.Get => HttpVerb.GET,
                PresignedAction.Put => HttpVerb.PUT,
                _ => HttpVerb.GET,
            },
            Protocol = InferProtocol(),
        };

        ApplyServerSideEncryption(request);

        try
        {
            var url = await _s3.GetPreSignedURLAsync(request).ConfigureAwait(false);
            return Result.Success(new Uri(url));
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 presign failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Presign", key);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the stream should be uploaded multipart.
    /// </summary>
    /// <param name="content">Source stream.</param>
    /// <returns><c>true</c> when seekable and at or above the multipart threshold.</returns>
    internal bool ShouldUseMultipart(Stream content)
    {
        if (!content.CanSeek)
        {
            // Non-seekable streams must use single-shot upload : we cannot know
            // the length up front.
            return false;
        }

        return content.Length >= _options.MultipartThresholdBytes;
    }

    private async Task PutSinglePartAsync(
        string storageKey,
        Stream content,
        ObjectMetadata? metadata,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = storageKey,
            InputStream = content,
            AutoCloseStream = false,
        };

        ApplyMetadata(request, metadata);
        ApplyServerSideEncryption(request);

        await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task PutMultipartAsync(
        string storageKey,
        Stream content,
        ObjectMetadata? metadata,
        CancellationToken cancellationToken)
    {
        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = _options.Bucket,
            Key = storageKey,
        };

        ApplyMetadata(initiateRequest, metadata);
        ApplyServerSideEncryption(initiateRequest);

        var initiate = await _s3.InitiateMultipartUploadAsync(initiateRequest, cancellationToken)
            .ConfigureAwait(false);

        var uploadId = initiate.UploadId;
        var partTags = new List<PartETag>();

        try
        {
            var buffer = new byte[MultipartChunkBytes];
            var partNumber = 1;

            while (true)
            {
                var read = await FillBufferAsync(content, buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                using var partStream = new MemoryStream(buffer, 0, read, writable: false);
                var partRequest = new UploadPartRequest
                {
                    BucketName = _options.Bucket,
                    Key = storageKey,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    InputStream = partStream,
                    PartSize = read,
                };

                var partResponse = await _s3.UploadPartAsync(partRequest, cancellationToken)
                    .ConfigureAwait(false);

                partTags.Add(new PartETag(partNumber, partResponse.ETag));
                partNumber++;
            }

            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = storageKey,
                UploadId = uploadId,
                PartETags = partTags,
            };

            await _s3.CompleteMultipartUploadAsync(completeRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort abort — never mask the original failure.
            try
            {
                await _s3.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest
                    {
                        BucketName = _options.Bucket,
                        Key = storageKey,
                        UploadId = uploadId,
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (AmazonS3Exception abortEx)
            {
                _logger.LogWarning(abortEx, "S3 multipart abort failed for {Key}", storageKey);
            }

            throw;
        }
    }

    private async Task<Result<ObjectInfo>> DescribeAsync(
        string tenantId,
        string storageKey,
        string tenantRelativeKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var head = await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _options.Bucket, Key = storageKey },
                cancellationToken).ConfigureAwait(false);

            return new ObjectInfo(
                Key: tenantRelativeKey,
                Size: head.ContentLength,
                ETag: NormalizeETag(head.ETag),
                ContentType: head.Headers?.ContentType,
                LastModified: NormalizeLastModified(head.LastModified),
                Metadata: ExtractUserMetadata(head.Metadata));
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "S3 post-upload HEAD failed for {Key}", storageKey);
            return S3ErrorMapping.Map(ex, "Put", tenantRelativeKey);
        }
    }

    private static async Task<int> FillBufferAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void ApplyMetadata(PutObjectRequest request, ObjectMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(metadata.ContentType))
        {
            request.ContentType = metadata.ContentType;
        }

        if (!string.IsNullOrEmpty(metadata.CacheControl))
        {
            request.Headers.CacheControl = metadata.CacheControl;
        }

        if (!string.IsNullOrEmpty(metadata.ContentDisposition))
        {
            request.Headers.ContentDisposition = metadata.ContentDisposition;
        }

        ApplyCustomMetadata(request.Metadata, metadata.Custom);
    }

    private static void ApplyMetadata(InitiateMultipartUploadRequest request, ObjectMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(metadata.ContentType))
        {
            request.ContentType = metadata.ContentType;
        }

        if (!string.IsNullOrEmpty(metadata.CacheControl))
        {
            request.Headers.CacheControl = metadata.CacheControl;
        }

        if (!string.IsNullOrEmpty(metadata.ContentDisposition))
        {
            request.Headers.ContentDisposition = metadata.ContentDisposition;
        }

        ApplyCustomMetadata(request.Metadata, metadata.Custom);
    }

    private static void ApplyCustomMetadata(
        MetadataCollection target,
        IReadOnlyDictionary<string, string>? custom)
    {
        if (custom is null)
        {
            return;
        }

        foreach (var (k, v) in custom)
        {
            target[k] = v;
        }
    }

    private static IReadOnlyDictionary<string, string>? ExtractUserMetadata(MetadataCollection? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawKey in metadata.Keys)
        {
            var value = metadata[rawKey];
            if (value is not null)
            {
                dict[rawKey] = value;
            }
        }

        return dict.Count == 0 ? null : dict;
    }

    private static string NormalizeETag(string? etag)
        => string.IsNullOrEmpty(etag) ? string.Empty : etag.Trim('"');

    private static DateTimeOffset NormalizeLastModified(DateTime? lastModified)
        => lastModified is { } lm
            ? new DateTimeOffset(DateTime.SpecifyKind(lm, DateTimeKind.Utc))
            : DateTimeOffset.MinValue;

    private void ApplyServerSideEncryption(PutObjectRequest request)
    {
        switch (_options.ServerSideEncryption)
        {
            case Options.ServerSideEncryption.Aes256:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                break;
            case Options.ServerSideEncryption.AwsKms:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
                if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
                {
                    request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
                }

                break;
            case Options.ServerSideEncryption.None:
            default:
                break;
        }
    }

    private void ApplyServerSideEncryption(InitiateMultipartUploadRequest request)
    {
        switch (_options.ServerSideEncryption)
        {
            case Options.ServerSideEncryption.Aes256:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                break;
            case Options.ServerSideEncryption.AwsKms:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
                if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
                {
                    request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
                }

                break;
            case Options.ServerSideEncryption.None:
            default:
                break;
        }
    }

    private void ApplyServerSideEncryption(GetPreSignedUrlRequest request)
    {
        switch (_options.ServerSideEncryption)
        {
            case Options.ServerSideEncryption.Aes256:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                break;
            case Options.ServerSideEncryption.AwsKms:
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
                if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
                {
                    request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
                }

                break;
            case Options.ServerSideEncryption.None:
            default:
                break;
        }
    }

    private Protocol InferProtocol()
    {
        // Honour the configured scheme when a custom endpoint is in play
        // (MinIO over HTTP, R2 over HTTPS, ...). Default to HTTPS for AWS.
        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl)
            && Uri.TryCreate(_options.ServiceUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            return Protocol.HTTP;
        }

        return Protocol.HTTPS;
    }

    private bool TryResolveTenant(out string tenantId, out Error error)
    {
        var ctx = _tenantAccessor.TenantContext;
        if (ctx is null || !ctx.HasTenant || string.IsNullOrWhiteSpace(ctx.TenantId))
        {
            tenantId = string.Empty;
            error = Error.Forbidden("S3.NoTenant", "No tenant in scope ; refusing operation.");
            return false;
        }

        if (!TenantKey.IsValidTenantId(ctx.TenantId))
        {
            tenantId = string.Empty;
            error = Error.Forbidden("S3.InvalidTenant", $"Tenant id '{ctx.TenantId}' is malformed.");
            return false;
        }

        tenantId = ctx.TenantId;
        error = default!;
        return true;
    }
}
