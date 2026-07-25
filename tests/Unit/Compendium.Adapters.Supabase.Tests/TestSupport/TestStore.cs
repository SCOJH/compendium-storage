// -----------------------------------------------------------------------
// <copyright file="TestStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Supabase.Tests.TestSupport;

/// <summary>
/// Builds a <see cref="SupabaseObjectStore"/> wired to a mocked HTTP handler for unit tests,
/// and exposes the canonical URLs/values so tests can assert request shapes.
/// </summary>
internal static class TestStore
{
    public const string StorageBaseUrl = "https://ref.supabase.co/storage/v1";
    public const string Bucket = "assets";
    public const string ApiKey = "test-service-role-key";
    public const string TenantId = "tenant-a";

    /// <summary>Absolute URL for the object endpoint of a tenant-relative key under <see cref="TenantId"/>.</summary>
    public static string ObjectUrl(string tenantRelativeKey) =>
        $"{StorageBaseUrl}/object/{Bucket}/{TenantId}/{tenantRelativeKey}";

    public static string InfoUrl(string tenantRelativeKey) =>
        $"{StorageBaseUrl}/object/info/{Bucket}/{TenantId}/{tenantRelativeKey}";

    public static string SignUrl(string tenantRelativeKey) =>
        $"{StorageBaseUrl}/object/sign/{Bucket}/{TenantId}/{tenantRelativeKey}";

    public static string UploadSignUrl(string tenantRelativeKey) =>
        $"{StorageBaseUrl}/object/upload/sign/{Bucket}/{TenantId}/{tenantRelativeKey}";

    public static string ListUrl() => $"{StorageBaseUrl}/object/list/{Bucket}";

    public static SupabaseObjectStore Create(
        MockHttpMessageHandler mockHttp,
        ITenantContextAccessor? tenant = null,
        SupabaseCapabilities? capabilities = null)
    {
        var context = new SupabaseStorageContext
        {
            StorageBaseUrl = StorageBaseUrl,
            ApiKey = ApiKey,
            Bucket = Bucket,
            DefaultPresignedUrlExpiry = TimeSpan.FromMinutes(15),
        };

        return new SupabaseObjectStore(
            new TestHttpClientFactory(mockHttp),
            context,
            capabilities ?? SupabaseCapabilities.ForProjectPlane(),
            tenant ?? new StaticTenantContextAccessor(TenantId),
            NullLogger<SupabaseObjectStore>.Instance);
    }
}
