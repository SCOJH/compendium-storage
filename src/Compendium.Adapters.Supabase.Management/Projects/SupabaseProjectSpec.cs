// -----------------------------------------------------------------------
// <copyright file="SupabaseProjectSpec.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Projects;

/// <summary>
/// The inputs to create a Supabase project on the Cloud Management API. The database
/// password is redacted in <see cref="ToString"/> so a logged spec never leaks it.
/// </summary>
public sealed record SupabaseProjectSpec
{
    /// <summary>Gets the human-readable project name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the target organization slug (the Management API's <c>organization_id</c>).
    /// </summary>
    public required string OrganizationSlug { get; init; }

    /// <summary>Gets the region (e.g. <c>eu-central-1</c>).</summary>
    public required string Region { get; init; }

    /// <summary>Gets the database password for the new project. Redacted in <see cref="ToString"/>.</summary>
    public required string DbPassword { get; init; }

    /// <summary>Gets the optional plan (e.g. <c>free</c> / <c>pro</c>); the org default applies when null.</summary>
    public string? Plan { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"SupabaseProjectSpec(Name={Name}, OrganizationSlug={OrganizationSlug}, Region={Region}, " +
        $"Plan={Plan ?? "(default)"}, DbPassword=***)";
}
