// -----------------------------------------------------------------------
// <copyright file="ISupabaseStorageFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.Connections;

namespace Compendium.Adapters.Supabase;

/// <summary>
/// Creates an <see cref="IObjectStore"/> bound to an explicit, per-call
/// <see cref="SupabaseConnection"/> and bucket. This is the seam a platform (e.g. Nexus)
/// uses when it holds many tenant projects behind one stateless adapter instance — the
/// options-configured store handles the single-project case.
/// </summary>
public interface ISupabaseStorageFactory
{
    /// <summary>
    /// Builds an <see cref="IObjectStore"/> for the given connection and bucket. The
    /// connection must carry a project key (<c>service_role</c> or <c>anon</c>) and a
    /// project or storage URL ; a management-only credential fails with
    /// <c>Supabase.NotConfigured</c>.
    /// </summary>
    /// <param name="connection">The Supabase connection (URLs + credential).</param>
    /// <param name="bucket">The bucket the store targets.</param>
    /// <returns>A bound <see cref="IObjectStore"/>, or a configuration error.</returns>
    Result<IObjectStore> Create(SupabaseConnection connection, string bucket);
}
