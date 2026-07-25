// -----------------------------------------------------------------------
// <copyright file="SupabaseObjectStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Security;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging;

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// Supabase Storage <see cref="IObjectStore"/> implementation over the native Storage
/// REST API, bound to the published <c>Compendium.Abstractions.Storage</c> port.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the tenant id from <see cref="ITenantContextAccessor"/> at every call,
/// prepends <c>{tenantId}/</c> to the key, and issues one HTTP request per operation via
/// a named <see cref="IHttpClientFactory"/> client. Authentication (<c>apikey</c> +
/// <c>Authorization: Bearer</c>) is attached per request so one client serves both the
/// options-configured store and any number of factory-built per-connection stores.
/// </para>
/// <para>
/// Also implements <see cref="ISupabaseObjectStoreExtras"/> (public URLs, image
/// transforms, signed upload URLs) — portable code depends on <see cref="IObjectStore"/>
/// only, Supabase-aware code casts to (or injects) the extras interface.
/// </para>
/// </remarks>
public sealed class SupabaseObjectStore : IObjectStore, ISupabaseObjectStoreExtras
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves per request.</summary>
    public const string HttpClientName = "compendium-supabase-storage";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SupabaseStorageContext _context;
    private readonly SupabaseCapabilities _capabilities;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<SupabaseObjectStore> _logger;

    /// <summary>
    /// Initialises a new <see cref="SupabaseObjectStore"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory (named client <see cref="HttpClientName"/>).</param>
    /// <param name="context">Resolved storage endpoint, key, and bucket.</param>
    /// <param name="capabilities">The capability matrix gating the Supabase-specific extras.</param>
    /// <param name="tenantAccessor">Tenant context accessor (resolved from DI).</param>
    /// <param name="logger">Diagnostic logger.</param>
    internal SupabaseObjectStore(
        IHttpClientFactory httpClientFactory,
        SupabaseStorageContext context,
        SupabaseCapabilities capabilities,
        ITenantContextAccessor tenantAccessor,
        ILogger<SupabaseObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(tenantAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _context = context;
        _capabilities = capabilities;
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

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "Put", out var storageKey, out var keyError))
        {
            return keyError;
        }

        var uploadedSize = content.CanSeek ? content.Length : (long?)null;
        var contentType = string.IsNullOrEmpty(metadata?.ContentType)
            ? "application/octet-stream"
            : metadata!.ContentType!;

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var streamContent = new StreamContent(content);
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            {
                streamContent.Headers.ContentType = mediaType;
            }
            else
            {
                streamContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            if (!string.IsNullOrEmpty(metadata?.ContentDisposition))
            {
                streamContent.Headers.TryAddWithoutValidation("Content-Disposition", metadata!.ContentDisposition);
            }

            using var request = NewRequest(HttpMethod.Post, ObjectPath(storageKey), streamContent);
            request.Headers.TryAddWithoutValidation("x-upsert", "true");

            // Supabase reads the object cache-control directive from the request header.
            if (!string.IsNullOrEmpty(metadata?.CacheControl))
            {
                request.Headers.TryAddWithoutValidation("cache-control", metadata!.CacheControl);
            }

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseErrorMapping
                    .MapAsync(response, "Put", key, _context.Bucket, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await DescribeAsync(http, storageKey, key, contentType, uploadedSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase PUT failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "Put");
        }
    }

    /// <inheritdoc />
    public async Task<Result<ObjectStream>> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "Get", out var storageKey, out var keyError))
        {
            return keyError;
        }

        try
        {
            // The client is intentionally not disposed here : the returned ObjectStream
            // owns the response (via HttpResponseStream) until the caller disposes it.
            var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewRequest(HttpMethod.Get, ObjectPath(storageKey));
            var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await SupabaseErrorMapping
                    .MapAsync(response, "Get", key, _context.Bucket, cancellationToken)
                    .ConfigureAwait(false);
                response.Dispose();
                return error;
            }

            Stream stream;
            try
            {
                stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The stream never reached the caller-owned ObjectStream : the response
                // must be released here or the connection leaks.
                response.Dispose();
                throw;
            }

            var info = new ObjectInfo(
                Key: key,
                Size: response.Content.Headers.ContentLength ?? 0,
                ETag: NormalizeETag(response.Headers.ETag?.Tag),
                ContentType: response.Content.Headers.ContentType?.MediaType,
                LastModified: response.Content.Headers.LastModified ?? DateTimeOffset.UtcNow);

            return new ObjectStream(new HttpResponseStream(stream, response), info);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase GET failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "Get");
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "Delete", out var storageKey, out var keyError))
        {
            return keyError;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewRequest(HttpMethod.Delete, ObjectPath(storageKey));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Deleting a non-existent key is success (published port contract).
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success();
            }

            return await SupabaseErrorMapping
                .MapAsync(response, "Delete", key, _context.Bucket, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase DELETE failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "Delete");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "Exists", out var storageKey, out var keyError))
        {
            return keyError;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewRequest(HttpMethod.Get, InfoPath(storageKey));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success(true);
            }

            // A missing object is not an error for Exists. storage-api may signal absence
            // with 404 or a not-found body on another status; both normalize to NotFound.
            var error = await SupabaseErrorMapping
                .MapAsync(response, "Exists", key, _context.Bucket, cancellationToken)
                .ConfigureAwait(false);
            return error.Type == ErrorType.NotFound ? Result.Success(false) : error;
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase HEAD/info failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "Exists");
        }
    }

    /// <inheritdoc />
    public async Task<Result<ListPage>> ListAsync(
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        var tenantPrefix = $"{tenantId}/";
        var subPrefix = options?.Prefix;
        var fullPrefix = string.IsNullOrEmpty(subPrefix) ? tenantPrefix : $"{tenantPrefix}{subPrefix}";
        var maxKeys = options?.MaxKeys ?? 1000;
        var offset = DecodeOffset(options?.ContinuationToken);

        var body = new SupabaseListRequest
        {
            Prefix = fullPrefix,
            Limit = maxKeys,
            Offset = offset,
        };

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewRequest(HttpMethod.Post, ListPath());
            request.Content = JsonContent.Create(body, options: JsonOptions);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseErrorMapping
                    .MapAsync(response, "List", subPrefix ?? string.Empty, _context.Bucket, cancellationToken)
                    .ConfigureAwait(false);
            }

            var records = await response.Content
                .ReadFromJsonAsync<List<SupabaseObjectRecord>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];

            var items = new List<ObjectInfo>();
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.Name))
                {
                    continue;
                }

                // Supabase list is single-level : folder pseudo-entries carry neither
                // an id nor metadata. Skip them ; surface only real objects.
                if (record.Id is null && record.Metadata is null)
                {
                    continue;
                }

                var childStorageKey = fullPrefix.EndsWith('/')
                    ? $"{fullPrefix}{record.Name}"
                    : $"{fullPrefix}/{record.Name}";

                items.Add(new ObjectInfo(
                    Key: TenantKey.Strip(tenantId, childStorageKey),
                    Size: record.Metadata?.Size ?? 0,
                    ETag: NormalizeETag(record.Metadata?.ETag),
                    ContentType: record.Metadata?.Mimetype,
                    LastModified: record.Metadata?.LastModified
                        ?? record.UpdatedAt
                        ?? DateTimeOffset.MinValue));
            }

            // Offset advances by the raw record count (before folder filtering).
            var nextToken = records.Count == maxKeys ? EncodeOffset(offset + records.Count) : null;
            return new ListPage(items, nextToken);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase LIST failed for prefix {Prefix}", fullPrefix);
            return SupabaseErrorMapping.MapException(ex, "List");
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

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "Presign", out var storageKey, out var keyError))
        {
            return keyError;
        }

        if (expiresIn <= TimeSpan.Zero)
        {
            return Error.Validation("Supabase.Presign.InvalidExpiry", "Expiry must be positive.");
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);

            if (action == PresignedAction.Put)
            {
                using var putRequest = NewRequest(HttpMethod.Post, UploadSignPath(storageKey));
                using var putResponse = await http.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
                if (!putResponse.IsSuccessStatusCode)
                {
                    return await SupabaseErrorMapping
                        .MapAsync(putResponse, "Presign", key, _context.Bucket, cancellationToken)
                        .ConfigureAwait(false);
                }

                var upload = await putResponse.Content
                    .ReadFromJsonAsync<SupabaseSignedUploadResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(upload?.Url))
                {
                    return Error.Failure("Supabase.Presign.EmptyResponse", "Supabase returned no signed upload URL.");
                }

                return AbsoluteFromRelative(upload!.Url!);
            }

            var body = new SupabaseSignRequest { ExpiresIn = (int)Math.Ceiling(expiresIn.TotalSeconds) };
            using var request = NewRequest(HttpMethod.Post, SignPath(storageKey));
            request.Content = JsonContent.Create(body, options: JsonOptions);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseErrorMapping
                    .MapAsync(response, "Presign", key, _context.Bucket, cancellationToken)
                    .ConfigureAwait(false);
            }

            var signed = await response.Content
                .ReadFromJsonAsync<SupabaseSignResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(signed?.SignedUrl))
            {
                return Error.Failure("Supabase.Presign.EmptyResponse", "Supabase returned no signed URL.");
            }

            return AbsoluteFromRelative(signed!.SignedUrl!);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase presign failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "Presign");
        }
    }

    /// <inheritdoc />
    public Result<Uri> GetPublicUrl(string key, ImageTransform? transform = null)
    {
        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "PublicUrl", out var storageKey, out var keyError))
        {
            return keyError;
        }

        var capability = transform is null ? SupabaseCapability.PublicUrl : SupabaseCapability.ImageTransform;
        var ensured = _capabilities.EnsureSupported(capability);
        if (ensured.IsFailure)
        {
            return ensured.Error;
        }

        var path = transform is null
            ? $"/object/public/{ObjectPathBody(storageKey)}"
            : $"/render/image/public/{ObjectPathBody(storageKey)}{BuildTransformQuery(transform)}";

        return BuildUrl(path);
    }

    /// <inheritdoc />
    public async Task<Result<SignedUpload>> CreateSignedUploadAsync(
        string key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveTenant(out var tenantId, out var tenantError))
        {
            return tenantError;
        }

        if (!TryComposeKey(tenantId, key, "SignedUpload", out var storageKey, out var keyError))
        {
            return keyError;
        }

        if (expiresIn <= TimeSpan.Zero)
        {
            return Error.Validation("Supabase.SignedUpload.InvalidExpiry", "Expiry must be positive.");
        }

        var ensured = _capabilities.EnsureSupported(SupabaseCapability.SignedUpload);
        if (ensured.IsFailure)
        {
            return ensured.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewRequest(HttpMethod.Post, UploadSignPath(storageKey));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseErrorMapping
                    .MapAsync(response, "SignedUpload", key, _context.Bucket, cancellationToken)
                    .ConfigureAwait(false);
            }

            var upload = await response.Content
                .ReadFromJsonAsync<SupabaseSignedUploadResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(upload?.Url) || string.IsNullOrEmpty(upload?.Token))
            {
                return Error.Failure("Supabase.SignedUpload.EmptyResponse", "Supabase returned an incomplete signed upload.");
            }

            return new SignedUpload(AbsoluteFromRelative(upload!.Url!), upload.Token!, key);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase signed-upload failed for {Key}", storageKey);
            return SupabaseErrorMapping.MapException(ex, "SignedUpload");
        }
    }

    private async Task<Result<ObjectInfo>> DescribeAsync(
        HttpClient http,
        string storageKey,
        string tenantRelativeKey,
        string requestContentType,
        long? uploadedSize,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = NewRequest(HttpMethod.Get, InfoPath(storageKey));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var record = await response.Content
                    .ReadFromJsonAsync<SupabaseObjectRecord>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return BuildInfo(record, tenantRelativeKey, requestContentType, uploadedSize);
            }

            // Some storage-api builds do not expose object info; the upload already
            // succeeded, so surface a best-effort ObjectInfo rather than failing PUT.
            _logger.LogDebug(
                "Supabase object-info unavailable ({Status}) for {Key}; returning best-effort ObjectInfo.",
                (int)response.StatusCode,
                storageKey);
            return BuildInfo(null, tenantRelativeKey, requestContentType, uploadedSize);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            _logger.LogDebug(ex, "Supabase object-info probe failed for {Key}; returning best-effort ObjectInfo.", storageKey);
            return BuildInfo(null, tenantRelativeKey, requestContentType, uploadedSize);
        }
    }

    private static ObjectInfo BuildInfo(
        SupabaseObjectRecord? record,
        string tenantRelativeKey,
        string requestContentType,
        long? uploadedSize)
    {
        return new ObjectInfo(
            Key: tenantRelativeKey,
            Size: record?.Metadata?.Size ?? uploadedSize ?? 0,
            ETag: NormalizeETag(record?.Metadata?.ETag),
            ContentType: record?.Metadata?.Mimetype ?? requestContentType,
            LastModified: record?.Metadata?.LastModified ?? record?.UpdatedAt ?? DateTimeOffset.UtcNow);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, BuildUrl(relativePath)) { Content = content };
        request.Headers.TryAddWithoutValidation("apikey", _context.ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_context.ApiKey}");
        return request;
    }

    private Uri BuildUrl(string relativePath) =>
        new($"{_context.StorageBaseUrl.TrimEnd('/')}{relativePath}");

    private Uri AbsoluteFromRelative(string relative)
    {
        if (Uri.TryCreate(relative, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        return new Uri($"{_context.StorageBaseUrl.TrimEnd('/')}/{relative.TrimStart('/')}");
    }

    private string ObjectPath(string storageKey) => $"/object/{ObjectPathBody(storageKey)}";

    private string InfoPath(string storageKey) => $"/object/info/{ObjectPathBody(storageKey)}";

    private string SignPath(string storageKey) => $"/object/sign/{ObjectPathBody(storageKey)}";

    private string UploadSignPath(string storageKey) => $"/object/upload/sign/{ObjectPathBody(storageKey)}";

    private string ListPath() => $"/object/list/{Uri.EscapeDataString(_context.Bucket)}";

    private string ObjectPathBody(string storageKey) =>
        $"{Uri.EscapeDataString(_context.Bucket)}/{EncodeKeyPath(storageKey)}";

    private static string EncodeKeyPath(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private static string BuildTransformQuery(ImageTransform transform)
    {
        var parts = new List<string>();
        if (transform.Width is { } w)
        {
            parts.Add($"width={w.ToString(CultureInfo.InvariantCulture)}");
        }

        if (transform.Height is { } h)
        {
            parts.Add($"height={h.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(transform.Resize))
        {
            parts.Add($"resize={Uri.EscapeDataString(transform.Resize)}");
        }

        if (transform.Quality is { } q)
        {
            parts.Add($"quality={q.ToString(CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static string NormalizeETag(string? etag) =>
        string.IsNullOrEmpty(etag) ? string.Empty : etag.Trim('"');

    private static string EncodeOffset(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static int DecodeOffset(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return int.TryParse(decoded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) && offset >= 0
                ? offset
                : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static bool IsTransport(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private bool TryComposeKey(string tenantId, string key, string operation, out string storageKey, out Error error)
    {
        try
        {
            storageKey = TenantKey.Compose(tenantId, key);
            error = default!;
            return true;
        }
        catch (ArgumentException ex)
        {
            storageKey = string.Empty;
            error = Error.Validation($"Supabase.{operation}.InvalidKey", ex.Message);
            return false;
        }
    }

    private bool TryResolveTenant(out string tenantId, out Error error)
    {
        var ctx = _tenantAccessor.TenantContext;
        if (ctx is null || !ctx.HasTenant || string.IsNullOrWhiteSpace(ctx.TenantId))
        {
            tenantId = string.Empty;
            error = Error.Forbidden("Supabase.NoTenant", "No tenant in scope ; refusing operation.");
            return false;
        }

        if (!TenantKey.IsValidTenantId(ctx.TenantId))
        {
            tenantId = string.Empty;
            error = Error.Forbidden("Supabase.InvalidTenant", $"Tenant id '{ctx.TenantId}' is malformed.");
            return false;
        }

        tenantId = ctx.TenantId;
        error = default!;
        return true;
    }
}
