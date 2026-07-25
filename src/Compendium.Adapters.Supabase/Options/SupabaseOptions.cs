// -----------------------------------------------------------------------
// <copyright file="SupabaseOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace Compendium.Adapters.Supabase.Options;

/// <summary>
/// Configuration for the Supabase Storage object-store adapter.
/// </summary>
/// <remarks>
/// <para>
/// One adapter, two planes : Supabase Cloud (<c>https://{ref}.supabase.co</c>) and
/// self-hosted (the Kong gateway root). Both talk the same native Storage REST API
/// with a project key (<see cref="ServiceRoleKey"/> or <see cref="AnonKey"/>).
/// </para>
/// <para>
/// Exactly one of <see cref="ServiceRoleKey"/> / <see cref="AnonKey"/> must be set —
/// enforced by an <see cref="Microsoft.Extensions.Options.IValidateOptions{TOptions}"/>.
/// The <see cref="ServiceRoleKey"/> bypasses row-level security ; the <see cref="AnonKey"/>
/// is constrained by RLS policies.
/// </para>
/// </remarks>
public sealed class SupabaseOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Compendium:Adapters:Supabase";

    /// <summary>
    /// Project URL — <c>https://{ref}.supabase.co</c> on Cloud, or the self-hosted
    /// gateway (Kong) root. Required.
    /// </summary>
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional direct <c>storage-api</c> URL override. When set, requests hit this
    /// URL instead of <c>{Url}/storage/v1</c>. Lets integration tests target a bare
    /// <c>supabase/storage-api</c> container without a Kong gateway in front.
    /// </summary>
    [Url]
    public string? StorageUrl { get; set; }

    /// <summary>
    /// Project <c>service_role</c> key — full-privilege project-plane access (bypasses
    /// RLS). Set exactly one of this or <see cref="AnonKey"/>.
    /// </summary>
    public string? ServiceRoleKey { get; set; }

    /// <summary>
    /// Project <c>anon</c> key — RLS-constrained project-plane access. Set exactly one
    /// of this or <see cref="ServiceRoleKey"/>.
    /// </summary>
    public string? AnonKey { get; set; }

    /// <summary>
    /// Default storage bucket. Required.
    /// </summary>
    [Required]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Default lifetime for presigned URLs. Default 15 minutes.
    /// </summary>
    public TimeSpan DefaultPresignedUrlExpiry { get; set; } = TimeSpan.FromMinutes(15);
}
