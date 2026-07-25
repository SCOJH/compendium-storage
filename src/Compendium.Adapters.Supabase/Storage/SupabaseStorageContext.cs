// -----------------------------------------------------------------------
// <copyright file="SupabaseStorageContext.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// The immutable, resolved connection details a <see cref="SupabaseObjectStore"/>
/// operates against — one storage endpoint, one credential, one bucket. Built from
/// <c>SupabaseOptions</c> (the DI path) or a <c>SupabaseConnection</c> (the factory path).
/// </summary>
internal sealed record SupabaseStorageContext
{
    /// <summary>Gets the storage-api base URL, without a trailing slash (e.g. <c>https://ref.supabase.co/storage/v1</c>).</summary>
    public required string StorageBaseUrl { get; init; }

    /// <summary>Gets the project key sent as <c>apikey</c> and bearer token.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Gets the bucket every operation targets.</summary>
    public required string Bucket { get; init; }

    /// <summary>Gets the default presigned-URL lifetime.</summary>
    public required TimeSpan DefaultPresignedUrlExpiry { get; init; }

    /// <summary>Returns a redacted representation — the key material never prints.</summary>
    public override string ToString() =>
        $"SupabaseStorageContext {{ StorageBaseUrl = {StorageBaseUrl}, ApiKey = ***, Bucket = {Bucket} }}";

    /// <summary>
    /// Resolves the storage-api base URL from a project URL and an optional direct
    /// storage-api override. The override wins ; otherwise the project URL is suffixed
    /// with <c>/storage/v1</c>. The result never carries a trailing slash.
    /// </summary>
    /// <param name="projectUrl">The project (or self-hosted gateway) URL.</param>
    /// <param name="storageUrlOverride">Optional direct storage-api URL.</param>
    /// <returns>The normalized storage-api base URL.</returns>
    public static string ResolveStorageBaseUrl(string? projectUrl, string? storageUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(storageUrlOverride))
        {
            return storageUrlOverride.TrimEnd('/');
        }

        return $"{(projectUrl ?? string.Empty).TrimEnd('/')}/storage/v1";
    }
}
