// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compendium.Storage.Encryption.DependencyInjection;

/// <summary>
/// DI registration helpers for the envelope-encryption decorator.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EncryptingObjectStore"/> as the <see cref="IEncryptedObjectStore"/>, over the
    /// registered <see cref="IObjectStore"/>, <see cref="IKeyEncryptionKeyProvider"/> and
    /// <see cref="ITenantContextAccessor"/>, which the application registers itself.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumStorageEncryption(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEncryptedObjectStore>(sp => new EncryptingObjectStore(
            sp.GetRequiredService<IObjectStore>(),
            sp.GetRequiredService<IKeyEncryptionKeyProvider>(),
            sp.GetRequiredService<ITenantContextAccessor>(),
            sp.GetService<ILogger<EncryptingObjectStore>>()));
        return services;
    }
}
