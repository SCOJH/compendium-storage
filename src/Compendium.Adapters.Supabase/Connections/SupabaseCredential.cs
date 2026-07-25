// -----------------------------------------------------------------------
// <copyright file="SupabaseCredential.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Connections;

/// <summary>
/// The credential material of a <see cref="SupabaseConnection"/>, as a closed union.
/// Every member redacts its material in <c>ToString()</c> so a logged connection never
/// leaks a secret.
/// </summary>
/// <remarks>
/// Project-plane HTTP (storage, PostgREST, ...) sends <c>apikey: {key}</c> plus
/// <c>Authorization: Bearer {key}</c>. Management-plane HTTP sends
/// <c>Authorization: Bearer {pat}</c> only.
/// </remarks>
public abstract record SupabaseCredential
{
    private SupabaseCredential()
    {
    }

    /// <summary>
    /// A personal access token for the Supabase Cloud Management API (control plane
    /// only). Reserved for the management package — not used by project-plane storage.
    /// </summary>
    /// <param name="Token">The token material. Redacted in <c>ToString()</c>.</param>
    public sealed record ManagementToken(string Token) : SupabaseCredential
    {
        /// <inheritdoc />
        public override string ToString() => "ManagementToken(***)";
    }

    /// <summary>
    /// A project <c>service_role</c> key — full-privilege project-plane calls that
    /// bypass row-level security.
    /// </summary>
    /// <param name="Key">The key material. Redacted in <c>ToString()</c>.</param>
    public sealed record ServiceRoleKey(string Key) : SupabaseCredential
    {
        /// <inheritdoc />
        public override string ToString() => "ServiceRoleKey(***)";
    }

    /// <summary>
    /// A project <c>anon</c> key — RLS-constrained project-plane calls.
    /// </summary>
    /// <param name="Key">The key material. Redacted in <c>ToString()</c>.</param>
    public sealed record AnonKey(string Key) : SupabaseCredential
    {
        /// <inheritdoc />
        public override string ToString() => "AnonKey(***)";
    }
}
