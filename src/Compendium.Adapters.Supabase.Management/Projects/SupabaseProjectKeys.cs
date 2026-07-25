// -----------------------------------------------------------------------
// <copyright file="SupabaseProjectKeys.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Projects;

/// <summary>
/// A project's resolved endpoint and API keys, read from the Management API. Every field
/// except the URL is secret material, so the whole record redacts in <see cref="ToString"/>.
/// </summary>
public sealed record SupabaseProjectKeys
{
    /// <summary>Gets the project root URL (<c>https://{ref}.supabase.co</c>).</summary>
    public required Uri ProjectUrl { get; init; }

    /// <summary>Gets the project <c>anon</c> key. Redacted in <see cref="ToString"/>.</summary>
    public required string AnonKey { get; init; }

    /// <summary>Gets the project <c>service_role</c> key. Redacted in <see cref="ToString"/>.</summary>
    public required string ServiceRoleKey { get; init; }

    /// <summary>
    /// Gets the Postgres connection string, when available. Redacted in <see cref="ToString"/>.
    /// </summary>
    public string? DbConnectionString { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"SupabaseProjectKeys(ProjectUrl={ProjectUrl}, AnonKey=***, ServiceRoleKey=***, " +
        $"DbConnectionString={(DbConnectionString is null ? "(none)" : "***")})";
}
