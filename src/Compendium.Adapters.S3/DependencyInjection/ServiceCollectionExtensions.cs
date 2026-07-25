// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Compendium.Abstractions.Storage;
using Compendium.Adapters.S3.Options;
using Compendium.Adapters.S3.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.S3.DependencyInjection;

/// <summary>
/// DI registration helpers for the S3 object-store adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the S3 adapter, binding configuration from the
    /// <see cref="S3Options.SectionName"/> section.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Source configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumS3(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<S3Options>()
            .Bind(configuration.GetSection(S3Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    /// <summary>
    /// Registers the S3 adapter using an inline options callback.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configure">Callback to populate <see cref="S3Options"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumS3(
        this IServiceCollection services,
        Action<S3Options> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<S3Options>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return RegisterCore(services);
    }

    private static IServiceCollection RegisterCore(IServiceCollection services)
    {
        services.AddSingleton<IAmazonS3>(BuildClient);
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        return services;
    }

    private static IAmazonS3 BuildClient(IServiceProvider sp)
    {
        var opt = sp.GetRequiredService<IOptions<S3Options>>().Value;
        return CreateClient(opt);
    }

    /// <summary>
    /// Builds the configured <see cref="IAmazonS3"/> for the provided options.
    /// Exposed for testing.
    /// </summary>
    /// <param name="opt">Adapter options.</param>
    /// <returns>Configured S3 client.</returns>
    internal static IAmazonS3 CreateClient(S3Options opt)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = opt.ForcePathStyle,
        };

        if (!string.IsNullOrWhiteSpace(opt.ServiceUrl))
        {
            config.ServiceURL = opt.ServiceUrl;
        }
        else if (!string.IsNullOrWhiteSpace(opt.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(opt.Region);
        }

        if (!string.IsNullOrWhiteSpace(opt.AccessKey) && !string.IsNullOrWhiteSpace(opt.SecretKey))
        {
            var credentials = new BasicAWSCredentials(opt.AccessKey, opt.SecretKey);
            return new AmazonS3Client(credentials, config);
        }

        return new AmazonS3Client(config);
    }
}
