// -----------------------------------------------------------------------
// <copyright file="SupabaseAdmin.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Buckets;
using Compendium.Adapters.Supabase.Management.Http;
using Compendium.Adapters.Supabase.Management.Projects;
using Compendium.Adapters.Supabase.Storage;
using Microsoft.Extensions.Logging;

namespace Compendium.Adapters.Supabase.Management;

/// <summary>
/// Default <see cref="ISupabaseAdmin"/> — a stateless singleton over one named
/// <see cref="IHttpClientFactory"/> client. Authentication is attached per request (never on
/// the client defaults) so a single instance serves any number of connections : the Cloud
/// Management API takes a Bearer personal access token, the Storage admin API takes the
/// project <c>service_role</c> key as <c>apikey</c> + Bearer.
/// </summary>
internal sealed class SupabaseAdmin : ISupabaseAdmin
{
    /// <summary>The named <see cref="HttpClient"/> the facade resolves per request.</summary>
    public const string HttpClientName = "compendium-supabase-management";

    /// <summary>The default Supabase Cloud Management API base URL.</summary>
    public const string ManagementDefaultBaseUrl = "https://api.supabase.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupabaseAdmin> _logger;

    /// <summary>Initialises a new <see cref="SupabaseAdmin"/>.</summary>
    /// <param name="httpClientFactory">HTTP client factory (named client <see cref="HttpClientName"/>).</param>
    /// <param name="logger">Diagnostic logger.</param>
    public SupabaseAdmin(IHttpClientFactory httpClientFactory, ILogger<SupabaseAdmin> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SupabaseProject>> CreateProjectAsync(
        SupabaseConnection connection,
        SupabaseProjectSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var gate = EnsureManagement(connection, SupabaseCapability.ProjectProvisioning, out var token);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        var body = new CreateProjectRequest
        {
            Name = spec.Name,
            OrganizationId = spec.OrganizationSlug,
            Region = spec.Region,
            DbPass = spec.DbPassword,
            Plan = spec.Plan,
        };

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var content = JsonContent.Create(body, options: JsonOptions);
            using var request = NewManagementRequest(
                HttpMethod.Post, $"{ManagementBaseUrl(connection)}/v1/projects", token!, content);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseManagementErrorMapping
                    .MapManagementFailureAsync(response, "CreateProject", cancellationToken)
                    .ConfigureAwait(false);
            }

            var dto = await response.Content
                .ReadFromJsonAsync<ProjectDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            // Provisioning is async upstream: absent status defaults to Provisioning.
            return MapProject(dto, fallbackRef: null, SupabaseProjectStatus.Provisioning, "CreateProject", spec);
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase CreateProject failed for {Name}", spec.Name);
            return SupabaseManagementErrorMapping.MapException(ex, "CreateProject");
        }
    }

    /// <inheritdoc />
    public async Task<Result<SupabaseProject>> GetProjectAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectRef))
        {
            return Error.Validation("Supabase.GetProject.InvalidRef", "A project ref is required.");
        }

        var gate = EnsureManagement(connection, SupabaseCapability.ProjectProvisioning, out var token);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewManagementRequest(
                HttpMethod.Get, $"{ManagementBaseUrl(connection)}/v1/projects/{Uri.EscapeDataString(projectRef)}", token!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return SupabaseErrors.ProjectNotFound(projectRef);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseManagementErrorMapping
                    .MapManagementFailureAsync(response, "GetProject", cancellationToken)
                    .ConfigureAwait(false);
            }

            var dto = await response.Content
                .ReadFromJsonAsync<ProjectDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return MapProject(dto, fallbackRef: projectRef, SupabaseProjectStatus.Unknown, "GetProject");
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase GetProject failed for {Ref}", projectRef);
            return SupabaseManagementErrorMapping.MapException(ex, "GetProject");
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteProjectAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectRef))
        {
            return Error.Validation("Supabase.DeleteProject.InvalidRef", "A project ref is required.");
        }

        var gate = EnsureManagement(connection, SupabaseCapability.ProjectProvisioning, out var token);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewManagementRequest(
                HttpMethod.Delete, $"{ManagementBaseUrl(connection)}/v1/projects/{Uri.EscapeDataString(projectRef)}", token!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Deleting a non-existent project is success (idempotent).
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success();
            }

            return await SupabaseManagementErrorMapping
                .MapManagementFailureAsync(response, "DeleteProject", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase DeleteProject failed for {Ref}", projectRef);
            return SupabaseManagementErrorMapping.MapException(ex, "DeleteProject");
        }
    }

    /// <inheritdoc />
    public async Task<Result<SupabaseProjectKeys>> GetProjectKeysAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectRef))
        {
            return Error.Validation("Supabase.GetProjectKeys.InvalidRef", "A project ref is required.");
        }

        var gate = EnsureManagement(connection, SupabaseCapability.ProjectKeys, out var token);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewManagementRequest(
                HttpMethod.Get,
                $"{ManagementBaseUrl(connection)}/v1/projects/{Uri.EscapeDataString(projectRef)}/api-keys?reveal=true",
                token!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return SupabaseErrors.ProjectNotFound(projectRef);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseManagementErrorMapping
                    .MapManagementFailureAsync(response, "GetProjectKeys", cancellationToken)
                    .ConfigureAwait(false);
            }

            var keys = await response.Content
                .ReadFromJsonAsync<List<ProjectApiKeyDto>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];

            var anon = FindKey(keys, "anon");
            var serviceRole = FindKey(keys, "service_role");
            if (string.IsNullOrEmpty(anon) || string.IsNullOrEmpty(serviceRole))
            {
                return SupabaseErrors.ProjectNotReady(projectRef, "API keys are not yet provisioned");
            }

            var projectUrl = BuildProjectUrl(projectRef);
            if (projectUrl is null)
            {
                return Error.Failure("Supabase.GetProjectKeys.InvalidRef", $"Could not derive a project URL from ref '{projectRef}'.");
            }

            return new SupabaseProjectKeys
            {
                ProjectUrl = projectUrl,
                AnonKey = anon!,
                ServiceRoleKey = serviceRole!,
            };
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase GetProjectKeys failed for {Ref}", projectRef);
            return SupabaseManagementErrorMapping.MapException(ex, "GetProjectKeys");
        }
    }

    /// <inheritdoc />
    public async Task<Result<SupabaseProject>> AttachProjectAsync(
        SupabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = connection.Credential switch
        {
            SupabaseCredential.ServiceRoleKey serviceRole => serviceRole.Key,
            SupabaseCredential.AnonKey anon => anon.Key,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return SupabaseErrors.NotConfigured(
                "attach requires a project ServiceRoleKey or AnonKey (a ManagementToken is control-plane only)");
        }

        if (connection.ProjectUrl is null && connection.StorageUrl is null)
        {
            return SupabaseErrors.NotConfigured("attach requires a ProjectUrl or StorageUrl");
        }

        var probeUrl = connection.ProjectUrl ?? connection.StorageUrl!;
        var storageBase = SupabaseStorageContext.ResolveStorageBaseUrl(
            connection.ProjectUrl?.ToString(),
            connection.StorageUrl?.ToString());

        // Primary probe: listing buckets verifies both reachability AND the credential.
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewStorageRequest(HttpMethod.Get, $"{storageBase.TrimEnd('/')}/bucket", apiKey!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return BuildAttachedProject(connection);
            }

            // A rejected credential is a hard failure — never fall through to the health probe.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return SupabaseErrors.AttachFailed(
                    probeUrl, $"the storage credential was rejected (HTTP {(int)response.StatusCode})");
            }
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogDebug(ex, "Supabase attach storage probe failed for {Url}", storageBase);
        }

        // Fallback probe: the GoTrue health endpoint, when a project/gateway URL is known.
        if (connection.ProjectUrl is not null)
        {
            var healthUrl = $"{connection.ProjectUrl.ToString().TrimEnd('/')}/auth/v1/health";
            try
            {
                using var http = _httpClientFactory.CreateClient(HttpClientName);
                using var request = NewStorageRequest(HttpMethod.Get, healthUrl, apiKey!);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                // Health 200 proves REACHABILITY only — GoTrue's health endpoint does not
                // validate the apikey on every proxy config, so a wrong key could still 200
                // here. Attach as Unknown (never Active): the caller must confirm health via
                // a credential-validating call (e.g. a storage op) before trusting the attach.
                return response.IsSuccessStatusCode
                    ? BuildAttachedProject(connection, SupabaseProjectStatus.Unknown, "attached-unverified")
                    : SupabaseErrors.AttachFailed(probeUrl, $"the health probe returned HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
            {
                _logger.LogDebug(ex, "Supabase attach health probe failed for {Url}", healthUrl);
                return SupabaseErrors.AttachFailed(probeUrl, $"the health probe was unreachable: {ex.Message}");
            }
        }

        return SupabaseErrors.AttachFailed(
            probeUrl, "the storage endpoint was unreachable and no ProjectUrl was set for a health fallback");
    }

    /// <inheritdoc />
    public async Task<Result<SupabaseBucket>> CreateBucketAsync(
        SupabaseConnection connection,
        SupabaseBucketSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(spec.Id))
        {
            return Error.Validation("Supabase.CreateBucket.InvalidId", "A bucket id is required.");
        }

        var gate = EnsureBucket(connection, out var apiKey, out var storageBase);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        var name = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name!;
        var body = new CreateBucketRequest
        {
            Id = spec.Id,
            Name = name,
            Public = spec.Public,
            FileSizeLimit = spec.FileSizeLimit,
            AllowedMimeTypes = spec.AllowedMimeTypes,
        };

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var content = JsonContent.Create(body, options: JsonOptions);
            using var request = NewStorageRequest(HttpMethod.Post, $"{storageBase!.TrimEnd('/')}/bucket", apiKey!, content);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseManagementErrorMapping
                    .MapStorageFailureAsync(response, "CreateBucket", spec.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Create returns only { name }; echo the requested spec as the created bucket.
            return new SupabaseBucket
            {
                Id = spec.Id,
                Name = name,
                Public = spec.Public,
                FileSizeLimit = spec.FileSizeLimit,
                AllowedMimeTypes = spec.AllowedMimeTypes,
            };
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase CreateBucket failed for {Bucket}", spec.Id);
            return SupabaseManagementErrorMapping.MapException(ex, "CreateBucket");
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SupabaseBucket>>> ListBucketsAsync(
        SupabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var gate = EnsureBucket(connection, out var apiKey, out var storageBase);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewStorageRequest(HttpMethod.Get, $"{storageBase!.TrimEnd('/')}/bucket", apiKey!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return await SupabaseManagementErrorMapping
                    .MapStorageFailureAsync(response, "ListBuckets", string.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }

            var dtos = await response.Content
                .ReadFromJsonAsync<List<BucketDto>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];

            IReadOnlyList<SupabaseBucket> buckets = dtos
                .Where(d => !string.IsNullOrEmpty(d.Id) || !string.IsNullOrEmpty(d.Name))
                .Select(MapBucket)
                .ToList();

            return Result.Success(buckets);
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase ListBuckets failed");
            return SupabaseManagementErrorMapping.MapException(ex, "ListBuckets");
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteBucketAsync(
        SupabaseConnection connection,
        string bucketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(bucketId))
        {
            return Error.Validation("Supabase.DeleteBucket.InvalidId", "A bucket id is required.");
        }

        var gate = EnsureBucket(connection, out var apiKey, out var storageBase);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = NewStorageRequest(
                HttpMethod.Delete, $"{storageBase!.TrimEnd('/')}/bucket/{Uri.EscapeDataString(bucketId)}", apiKey!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Deleting a non-existent bucket is success (idempotent).
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success();
            }

            return await SupabaseManagementErrorMapping
                .MapStorageFailureAsync(response, "DeleteBucket", bucketId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Supabase DeleteBucket failed for {Bucket}", bucketId);
            return SupabaseManagementErrorMapping.MapException(ex, "DeleteBucket");
        }
    }

    private static string ManagementBaseUrl(SupabaseConnection connection) =>
        (connection.ManagementUrl?.ToString() ?? ManagementDefaultBaseUrl).TrimEnd('/');

    /// <summary>
    /// Gates a management-plane operation and extracts the personal access token. On success
    /// <paramref name="token"/> is the non-empty management token.
    /// </summary>
    private static Result EnsureManagement(
        SupabaseConnection connection,
        SupabaseCapability capability,
        out string? token)
    {
        token = null;

        var ensured = SupabaseCapabilities.For(connection).EnsureSupported(capability);
        if (ensured.IsFailure)
        {
            return ensured.Error;
        }

        if (connection.Credential is not SupabaseCredential.ManagementToken managementToken
            || string.IsNullOrWhiteSpace(managementToken.Token))
        {
            return SupabaseErrors.ManagementUnauthorized();
        }

        token = managementToken.Token;
        return Result.Success();
    }

    /// <summary>
    /// Gates a bucket operation on <see cref="SupabaseCapability.BucketManagement"/> and
    /// resolves the <c>service_role</c> key + storage base URL.
    /// </summary>
    private static Result EnsureBucket(
        SupabaseConnection connection,
        out string? apiKey,
        out string? storageBase)
    {
        apiKey = null;
        storageBase = null;

        var ensured = SupabaseCapabilities.For(connection).EnsureSupported(SupabaseCapability.BucketManagement);
        if (ensured.IsFailure)
        {
            return ensured.Error;
        }

        if (connection.Credential is not SupabaseCredential.ServiceRoleKey serviceRole
            || string.IsNullOrWhiteSpace(serviceRole.Key))
        {
            return SupabaseErrors.NotConfigured("bucket management requires a service_role key");
        }

        if (connection.ProjectUrl is null && connection.StorageUrl is null)
        {
            return SupabaseErrors.NotConfigured("a ProjectUrl or StorageUrl is required for bucket operations");
        }

        apiKey = serviceRole.Key;
        storageBase = SupabaseStorageContext.ResolveStorageBaseUrl(
            connection.ProjectUrl?.ToString(),
            connection.StorageUrl?.ToString());
        return Result.Success();
    }

    private static Result<SupabaseProject> MapProject(
        ProjectDto? dto,
        string? fallbackRef,
        SupabaseProjectStatus fallbackStatus,
        string operation,
        SupabaseProjectSpec? spec = null)
    {
        var refId = FirstNonEmpty(dto?.Ref, dto?.Id, fallbackRef);
        if (string.IsNullOrWhiteSpace(refId))
        {
            return Error.Failure($"Supabase.{operation}.EmptyResponse", "Supabase returned no project ref.");
        }

        // A recognized upstream status wins; an absent one falls back (Provisioning on create).
        var status = string.IsNullOrWhiteSpace(dto?.Status)
            ? fallbackStatus
            : SupabaseProjectStatusMapper.Map(dto!.Status);

        return new SupabaseProject
        {
            Ref = refId!,
            Name = FirstNonEmpty(dto?.Name, spec?.Name, refId)!,
            Status = status,
            Plane = "cloud",
            OrganizationSlug = FirstNonEmpty(dto?.OrganizationId, spec?.OrganizationSlug),
            Region = FirstNonEmpty(dto?.Region, spec?.Region),
            ProjectUrl = BuildProjectUrl(refId!),
            CreatedAt = dto?.CreatedAt,
            RawStatus = dto?.Status,
        };
    }

    private static SupabaseBucket MapBucket(BucketDto dto)
    {
        var id = FirstNonEmpty(dto.Id, dto.Name)!;
        return new SupabaseBucket
        {
            Id = id,
            Name = FirstNonEmpty(dto.Name, dto.Id)!,
            Public = dto.Public,
            FileSizeLimit = dto.FileSizeLimit,
            AllowedMimeTypes = dto.AllowedMimeTypes,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
        };
    }

    private static SupabaseProject BuildAttachedProject(
        SupabaseConnection connection,
        SupabaseProjectStatus status = SupabaseProjectStatus.Active,
        string rawStatus = "attached")
    {
        var url = connection.ProjectUrl ?? connection.StorageUrl!;
        var host = url.Host;
        return new SupabaseProject
        {
            Ref = host,
            Name = host,
            Status = status,
            Plane = "self-hosted",
            ProjectUrl = connection.ProjectUrl,
            RawStatus = rawStatus,
        };
    }

    private static string? FindKey(IEnumerable<ProjectApiKeyDto> keys, string name) =>
        keys.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))?.ApiKey;

    private static Uri? BuildProjectUrl(string projectRef) =>
        Uri.TryCreate($"https://{projectRef}.supabase.co", UriKind.Absolute, out var uri) ? uri : null;

    private static HttpRequestMessage NewManagementRequest(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }

    private static HttpRequestMessage NewStorageRequest(HttpMethod method, string url, string apiKey, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        return request;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static bool IsTransientFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || ex is JsonException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
