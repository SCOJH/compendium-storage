// -----------------------------------------------------------------------
// <copyright file="SupabaseCapability.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Capabilities;

/// <summary>
/// A capability a Supabase connection may or may not support, depending on its plane
/// (cloud vs self-hosted) and credential (project key vs management token).
/// </summary>
public enum SupabaseCapability
{
    /// <summary>Object storage read/write over the Storage REST API.</summary>
    ObjectStorage,

    /// <summary>Time-limited presigned download (GET) URLs.</summary>
    PresignedGet,

    /// <summary>Single-use signed upload URLs / tokens.</summary>
    SignedUpload,

    /// <summary>Public (no-expiry) object URLs for public buckets.</summary>
    PublicUrl,

    /// <summary>On-the-fly image transformation URLs.</summary>
    ImageTransform,

    /// <summary>Bucket create / list / delete via the Storage REST API.</summary>
    BucketManagement,

    /// <summary>Provisioning of Supabase projects (Cloud Management API).</summary>
    ProjectProvisioning,

    /// <summary>Reading a project's API keys (Cloud Management API).</summary>
    ProjectKeys,

    /// <summary>Pausing / resuming a project (Cloud Management API).</summary>
    ProjectPause,

    /// <summary>Realtime broadcast (deferred to a future phase).</summary>
    RealtimeBroadcast,
}

/// <summary>
/// The support level a Supabase connection declares for a <see cref="SupabaseCapability"/>.
/// </summary>
public enum SupabaseCapabilityLevel
{
    /// <summary>The capability is not available on this connection.</summary>
    None,

    /// <summary>The capability is available with limitations (documented in CAPABILITIES.md).</summary>
    Partial,

    /// <summary>The capability is fully supported.</summary>
    Full,
}

/// <summary>
/// The declared support for a single capability.
/// </summary>
/// <param name="Level">The support level.</param>
/// <param name="Limitation">
/// A short human-readable limitation note, expected when <paramref name="Level"/> is
/// <see cref="SupabaseCapabilityLevel.Partial"/> or <see cref="SupabaseCapabilityLevel.None"/>.
/// </param>
public sealed record SupabaseCapabilitySupport(SupabaseCapabilityLevel Level, string? Limitation = null);
