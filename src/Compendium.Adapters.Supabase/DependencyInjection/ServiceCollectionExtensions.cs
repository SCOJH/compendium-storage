// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Configuration;
using Compendium.Adapters.Supabase.Options;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Supabase.DependencyInjection;

/// <summary>
/// DI registration helpers for the Supabase Storage object-store adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Supabase Storage adapter, binding configuration from the
    /// <see cref="SupabaseOptions.SectionName"/> section.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Source configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumSupabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Registers the Supabase Storage adapter using an inline options callback.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configure">Callback to populate <see cref="SupabaseOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumSupabase(
        this IServiceCollection services,
        Action<SupabaseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SupabaseOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Registers the Supabase Storage adapter from a platform-injected resource, binding the
    /// <c>Resources__{resourceName}__Url/AnonKey/ServiceRoleKey/DbConnectionString/Bucket</c>
    /// environment-variable convention (see <see cref="SupabaseResourceBinding"/>).
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Source configuration.</param>
    /// <param name="resourceName">The injected resource name.</param>
    /// <param name="bucket">
    /// Overrides the bucket. When <see langword="null"/>, the bucket is taken from
    /// <c>Resources__{resourceName}__Bucket</c>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at registration time when the resource is missing its URL, a usable key, or a bucket.
    /// </exception>
    public static IServiceCollection AddCompendiumSupabaseFromResource(
        this IServiceCollection services,
        IConfiguration configuration,
        string resourceName,
        string? bucket = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var binding = SupabaseResourceBinding.FromConfiguration(configuration, resourceName);
        if (binding.IsFailure)
        {
            throw new InvalidOperationException(binding.Error.Message);
        }

        var resource = binding.Value;
        var effectiveBucket = string.IsNullOrWhiteSpace(bucket) ? resource.Bucket : bucket;
        if (string.IsNullOrWhiteSpace(effectiveBucket))
        {
            throw new InvalidOperationException(
                $"Supabase resource '{resourceName}' has no bucket : pass one explicitly or set " +
                $"{SupabaseResourceBinding.ResourcesSection}__{resourceName}__Bucket.");
        }

        return services.AddCompendiumSupabase(options =>
        {
            options.Url = resource.Url;

            // SupabaseOptions requires exactly one key ; prefer the service_role key.
            if (!string.IsNullOrWhiteSpace(resource.ServiceRoleKey))
            {
                options.ServiceRoleKey = resource.ServiceRoleKey;
                options.AnonKey = null;
            }
            else
            {
                options.AnonKey = resource.AnonKey;
                options.ServiceRoleKey = null;
            }

            options.Bucket = effectiveBucket!;
        });
    }

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SupabaseOptions>, SupabaseOptionsValidator>());

        services.AddHttpClient(SupabaseObjectStore.HttpClientName);

        // One concrete instance mapped to both the portable port and the Supabase extras.
        services.TryAddSingleton(BuildObjectStore);
        services.TryAddSingleton<IObjectStore>(sp => sp.GetRequiredService<SupabaseObjectStore>());
        services.TryAddSingleton<ISupabaseObjectStoreExtras>(sp => sp.GetRequiredService<SupabaseObjectStore>());
        services.TryAddSingleton<ISupabaseStorageFactory, SupabaseStorageFactory>();

        return services;
    }

    private static SupabaseObjectStore BuildObjectStore(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
        var apiKey = !string.IsNullOrWhiteSpace(options.ServiceRoleKey)
            ? options.ServiceRoleKey!
            : options.AnonKey!;

        var context = new SupabaseStorageContext
        {
            StorageBaseUrl = SupabaseStorageContext.ResolveStorageBaseUrl(options.Url, options.StorageUrl),
            ApiKey = apiKey,
            Bucket = options.Bucket,
            DefaultPresignedUrlExpiry = options.DefaultPresignedUrlExpiry,
        };

        return new SupabaseObjectStore(
            sp.GetRequiredService<IHttpClientFactory>(),
            context,
            SupabaseCapabilities.ForProjectPlane(),
            sp.GetRequiredService<ITenantContextAccessor>(),
            sp.GetRequiredService<ILogger<SupabaseObjectStore>>());
    }
}
