// -----------------------------------------------------------------------
// <copyright file="SupabaseBucket.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Buckets;

/// <summary>
/// A storage bucket as returned by the Storage admin API.
/// </summary>
public sealed record SupabaseBucket
{
    /// <summary>Gets the bucket id (immutable primary key).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether the bucket is public.</summary>
    public bool Public { get; init; }

    /// <summary>Gets the per-object size limit in bytes, when set.</summary>
    public long? FileSizeLimit { get; init; }

    /// <summary>Gets the allow-list of MIME types, when set.</summary>
    public IReadOnlyList<string>? AllowedMimeTypes { get; init; }

    /// <summary>Gets the creation timestamp, when reported.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets the last-updated timestamp, when reported.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
