// -----------------------------------------------------------------------
// <copyright file="SupabaseConnection.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Connections;

/// <summary>
/// Identifies which Supabase surface to talk to and with what credential. Every
/// <see cref="ISupabaseStorageFactory"/> (and, later, management) call takes one
/// explicitly — a single adapter instance serves any number of tenant projects.
/// </summary>
public sealed record SupabaseConnection
{
    /// <summary>
    /// Gets the project root URL — <c>https://{ref}.supabase.co</c> on Cloud, or the
    /// self-hosted (Kong) root URL. <see langword="null"/> only for pure
    /// management-plane calls (e.g. <c>CreateProject</c>).
    /// </summary>
    public Uri? ProjectUrl { get; init; }

    /// <summary>
    /// Gets the optional direct <c>storage-api</c> URL override. Default is
    /// <c>{ProjectUrl}/storage/v1</c>. Lets integration tests target a bare
    /// <c>supabase/storage-api</c> container without a Kong gateway.
    /// </summary>
    public Uri? StorageUrl { get; init; }

    /// <summary>
    /// Gets the Management API base. <see langword="null"/> targets Supabase Cloud
    /// (<c>https://api.supabase.com</c>). Self-hosted has no Management API — leave
    /// this <see langword="null"/> and don't call management-gated methods (the
    /// capability matrix enforces this as a <see cref="Result"/>). Reserved for the
    /// management package.
    /// </summary>
    public Uri? ManagementUrl { get; init; }

    /// <summary>
    /// Gets the credential used for this connection.
    /// </summary>
    public required SupabaseCredential Credential { get; init; }
}
