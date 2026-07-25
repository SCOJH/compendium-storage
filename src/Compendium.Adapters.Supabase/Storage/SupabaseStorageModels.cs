// -----------------------------------------------------------------------
// <copyright file="SupabaseStorageModels.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>Request body for <c>POST /object/list/{bucket}</c>.</summary>
internal sealed record SupabaseListRequest
{
    [JsonPropertyName("prefix")]
    public required string Prefix { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    [JsonPropertyName("sortBy")]
    public SupabaseListSortBy SortBy { get; init; } = new();
}

/// <summary>Sort clause for <see cref="SupabaseListRequest"/>.</summary>
internal sealed record SupabaseListSortBy
{
    [JsonPropertyName("column")]
    public string Column { get; init; } = "name";

    [JsonPropertyName("order")]
    public string Order { get; init; } = "asc";
}

/// <summary>Request body for <c>POST /object/sign/{bucket}/{path}</c>.</summary>
internal sealed record SupabaseSignRequest
{
    [JsonPropertyName("expiresIn")]
    public required int ExpiresIn { get; init; }
}

/// <summary>Response of <c>POST /object/sign/{bucket}/{path}</c>.</summary>
internal sealed record SupabaseSignResponse
{
    [JsonPropertyName("signedURL")]
    public string? SignedUrl { get; init; }
}

/// <summary>Response of <c>POST /object/upload/sign/{bucket}/{path}</c>.</summary>
internal sealed record SupabaseSignedUploadResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>
/// A storage object row as returned by <c>/object/list/{bucket}</c> and
/// <c>/object/info/{bucket}/{path}</c>. Fields are tolerant : object metadata lives
/// under a nested <c>metadata</c> object on most storage-api versions.
/// </summary>
internal sealed record SupabaseObjectRecord
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("metadata")]
    public SupabaseObjectMetadataRecord? Metadata { get; init; }
}

/// <summary>The nested <c>metadata</c> object of a <see cref="SupabaseObjectRecord"/>.</summary>
internal sealed record SupabaseObjectMetadataRecord
{
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    [JsonPropertyName("mimetype")]
    public string? Mimetype { get; init; }

    [JsonPropertyName("eTag")]
    public string? ETag { get; init; }

    [JsonPropertyName("cacheControl")]
    public string? CacheControl { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; init; }
}
