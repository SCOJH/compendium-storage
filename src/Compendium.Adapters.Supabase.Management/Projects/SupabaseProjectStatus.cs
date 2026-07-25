// -----------------------------------------------------------------------
// <copyright file="SupabaseProjectStatus.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Projects;

/// <summary>
/// The coarse lifecycle state of a Supabase project, normalized from the many raw states
/// the Cloud Management API reports (<c>ACTIVE_HEALTHY</c>, <c>COMING_UP</c>,
/// <c>INIT_FAILED</c>, ...). A provisioning saga polls until <see cref="Active"/> and treats
/// <see cref="Failed"/> / <see cref="Deleted"/> as terminal.
/// </summary>
public enum SupabaseProjectStatus
{
    /// <summary>The upstream state was absent or not recognized.</summary>
    Unknown = 0,

    /// <summary>The project is coming up, restoring, upgrading, or otherwise not yet ready.</summary>
    Provisioning,

    /// <summary>The project is up and healthy — usable.</summary>
    Active,

    /// <summary>
    /// The project reached a non-usable, non-self-recovering state — initialization/restore
    /// failed, or it is paused/inactive (not running and will not resume on its own).
    /// </summary>
    Failed,

    /// <summary>The project is being torn down / paused (transitioning out of active).</summary>
    Deprovisioning,

    /// <summary>The project has been removed.</summary>
    Deleted,
}

/// <summary>
/// Maps the raw Supabase Cloud Management API project state string onto the normalized
/// <see cref="SupabaseProjectStatus"/>.
/// </summary>
public static class SupabaseProjectStatusMapper
{
    /// <summary>
    /// Normalizes a raw upstream status string. Case- and whitespace-insensitive; an
    /// unrecognized or blank value maps to <see cref="SupabaseProjectStatus.Unknown"/>.
    /// </summary>
    /// <param name="rawStatus">The <c>status</c> field returned by the Management API.</param>
    /// <returns>The normalized status.</returns>
    public static SupabaseProjectStatus Map(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return SupabaseProjectStatus.Unknown;
        }

        return rawStatus.Trim().ToUpperInvariant() switch
        {
            "ACTIVE_HEALTHY" => SupabaseProjectStatus.Active,

            "COMING_UP"
                or "INITIALIZING"
                or "RESTORING"
                or "RESTARTING"
                or "UPGRADING"
                or "UNPAUSING"
                or "ACTIVE_UNHEALTHY" => SupabaseProjectStatus.Provisioning,

            "GOING_DOWN"
                or "PAUSING"
                or "REMOVING" => SupabaseProjectStatus.Deprovisioning,

            "REMOVED" => SupabaseProjectStatus.Deleted,

            "INIT_FAILED"
                or "RESTORE_FAILED"
                or "RESTART_FAILED"
                or "UPGRADE_FAILED"
                or "INACTIVE"
                or "PAUSED" => SupabaseProjectStatus.Failed,

            _ => SupabaseProjectStatus.Unknown,
        };
    }
}
