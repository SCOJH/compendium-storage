// -----------------------------------------------------------------------
// <copyright file="ManagementDtos.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Compendium.Adapters.Supabase.Management.Http;

/// <summary>Request body for <c>POST /v1/projects</c>.</summary>
internal sealed record CreateProjectRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("organization_id")]
    public required string OrganizationId { get; init; }

    [JsonPropertyName("region")]
    public required string Region { get; init; }

    [JsonPropertyName("db_pass")]
    public required string DbPass { get; init; }

    [JsonPropertyName("plan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Plan { get; init; }

    /// <summary>Redacted — the DB password must never reach logs via record ToString.</summary>
    public override string ToString() =>
        $"CreateProjectRequest {{ Name = {Name}, OrganizationId = {OrganizationId}, Region = {Region}, DbPass = ***, Plan = {Plan} }}";
}

/// <summary>Project object returned by <c>POST /v1/projects</c> and <c>GET /v1/projects/{ref}</c>.</summary>
internal sealed record ProjectDto
{
    // The Management API returns the project ref as "id"; accept "ref" as well for forward-compat.
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; init; }

    [JsonPropertyName("region")]
    public string? Region { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>An entry of the <c>GET /v1/projects/{ref}/api-keys</c> array.</summary>
internal sealed record ProjectApiKeyDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    /// <summary>Redacted — this DTO carries the revealed anon/service_role keys.</summary>
    public override string ToString() => $"ProjectApiKeyDto {{ Name = {Name}, ApiKey = *** }}";
}

/// <summary>Request body for <c>POST /bucket</c> (Storage admin API).</summary>
internal sealed record CreateBucketRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("public")]
    public bool Public { get; init; }

    [JsonPropertyName("file_size_limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FileSizeLimit { get; init; }

    [JsonPropertyName("allowed_mime_types")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedMimeTypes { get; init; }
}

/// <summary>Bucket object returned by <c>GET /bucket</c> (Storage admin API).</summary>
internal sealed record BucketDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("public")]
    public bool Public { get; init; }

    [JsonPropertyName("file_size_limit")]
    public long? FileSizeLimit { get; init; }

    [JsonPropertyName("allowed_mime_types")]
    public IReadOnlyList<string>? AllowedMimeTypes { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }
}
