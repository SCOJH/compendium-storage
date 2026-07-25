// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compendium.Adapters.Supabase.Management.DependencyInjection;

/// <summary>
/// DI registration helpers for the Supabase management-plane facade.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISupabaseAdmin"/> as a stateless singleton over the named
    /// <c>compendium-supabase-management</c> HTTP client. Authentication is attached per
    /// request (never on the client), so one registration serves every connection. Safe to
    /// call alongside <c>AddCompendiumSupabase</c>; both may be registered in the same
    /// container.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumSupabaseManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(SupabaseAdmin.HttpClientName);
        services.TryAddSingleton<ISupabaseAdmin, SupabaseAdmin>();

        return services;
    }
}
