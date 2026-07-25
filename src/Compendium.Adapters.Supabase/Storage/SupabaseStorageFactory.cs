// -----------------------------------------------------------------------
// <copyright file="SupabaseStorageFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Connections;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging;

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// Default <see cref="ISupabaseStorageFactory"/>. Stateless singleton : every call
/// builds a fresh <see cref="SupabaseObjectStore"/> over the shared named HTTP client,
/// with credentials taken from the supplied <see cref="SupabaseConnection"/>.
/// </summary>
internal sealed class SupabaseStorageFactory : ISupabaseStorageFactory
{
    private static readonly TimeSpan DefaultPresignedUrlExpiry = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILoggerFactory _loggerFactory;

    public SupabaseStorageFactory(
        IHttpClientFactory httpClientFactory,
        ITenantContextAccessor tenantAccessor,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(tenantAccessor);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _httpClientFactory = httpClientFactory;
        _tenantAccessor = tenantAccessor;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public Result<IObjectStore> Create(SupabaseConnection connection, string bucket)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(bucket))
        {
            return SupabaseErrors.NotConfigured("a bucket is required");
        }

        var apiKey = connection.Credential switch
        {
            SupabaseCredential.ServiceRoleKey serviceRole => serviceRole.Key,
            SupabaseCredential.AnonKey anon => anon.Key,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return SupabaseErrors.NotConfigured(
                "a project ServiceRoleKey or AnonKey is required for storage operations (ManagementToken is control-plane only)");
        }

        if (connection.ProjectUrl is null && connection.StorageUrl is null)
        {
            return SupabaseErrors.NotConfigured("a ProjectUrl or StorageUrl is required");
        }

        var context = new SupabaseStorageContext
        {
            StorageBaseUrl = SupabaseStorageContext.ResolveStorageBaseUrl(
                connection.ProjectUrl?.ToString(),
                connection.StorageUrl?.ToString()),
            ApiKey = apiKey!,
            Bucket = bucket,
            DefaultPresignedUrlExpiry = DefaultPresignedUrlExpiry,
        };

        var store = new SupabaseObjectStore(
            _httpClientFactory,
            context,
            SupabaseCapabilities.For(connection),
            _tenantAccessor,
            _loggerFactory.CreateLogger<SupabaseObjectStore>());

        return Result.Success<IObjectStore>(store);
    }
}
