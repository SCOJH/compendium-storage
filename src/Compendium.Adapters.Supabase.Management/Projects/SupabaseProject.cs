// -----------------------------------------------------------------------
// <copyright file="SupabaseProject.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Projects;

/// <summary>
/// A Supabase project as seen by the control plane — either a Cloud project (from the
/// Management API) or a bring-your-own / self-hosted project resolved by
/// <c>AttachProjectAsync</c>. Carries no secret material.
/// </summary>
public sealed record SupabaseProject
{
    /// <summary>
    /// Gets the project reference. On Cloud this is the Supabase <c>ref</c>
    /// (<c>{ref}.supabase.co</c>); for an attached self-hosted project it is a stable
    /// identifier derived from the project host.
    /// </summary>
    public required string Ref { get; init; }

    /// <summary>Gets the project name (the host, for an attached self-hosted project).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the normalized lifecycle status.</summary>
    public required SupabaseProjectStatus Status { get; init; }

    /// <summary>Gets the connection plane (<c>"cloud"</c> / <c>"self-hosted"</c>).</summary>
    public required string Plane { get; init; }

    /// <summary>Gets the organization slug, when known (Cloud only).</summary>
    public string? OrganizationSlug { get; init; }

    /// <summary>Gets the region, when known (Cloud only).</summary>
    public string? Region { get; init; }

    /// <summary>Gets the project root URL (<c>https://{ref}.supabase.co</c> or the self-hosted root).</summary>
    public Uri? ProjectUrl { get; init; }

    /// <summary>Gets the creation timestamp, when reported.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Gets the raw upstream status string (<c>ACTIVE_HEALTHY</c>, ...) for diagnostics —
    /// retained so operators can distinguish states the coarse <see cref="Status"/> collapses.
    /// </summary>
    public string? RawStatus { get; init; }
}
