// -----------------------------------------------------------------------
// <copyright file="SupabaseBucketSpec.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Buckets;

/// <summary>
/// The inputs to create a storage bucket via the Storage admin API (<c>POST /bucket</c>).
/// </summary>
public sealed record SupabaseBucketSpec
{
    /// <summary>Gets the bucket id (the immutable primary key; also the default name).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display name; defaults to <see cref="Id"/> when null.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether the bucket is public (no-expiry object URLs). Defaults to private.</summary>
    public bool Public { get; init; }

    /// <summary>Gets the optional per-object size limit in bytes.</summary>
    public long? FileSizeLimit { get; init; }

    /// <summary>Gets the optional allow-list of MIME types accepted by the bucket.</summary>
    public IReadOnlyList<string>? AllowedMimeTypes { get; init; }
}
