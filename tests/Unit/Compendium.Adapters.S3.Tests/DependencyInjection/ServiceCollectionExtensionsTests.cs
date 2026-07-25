// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Amazon.S3;
using Compendium.Abstractions.Storage;
using Compendium.Adapters.S3.DependencyInjection;
using Compendium.Adapters.S3.Options;
using Compendium.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Adapters.S3.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumS3_WithConfiguration_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor>(_ => Substitute.For<ITenantContextAccessor>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compendium:Adapters:S3:Bucket"] = "b1",
                ["Compendium:Adapters:S3:Region"] = "us-east-1",
                ["Compendium:Adapters:S3:AccessKey"] = "AKIA-TEST",
                ["Compendium:Adapters:S3:SecretKey"] = "secret",
            })
            .Build();

        // Act
        var actual = services.AddCompendiumS3(configuration);
        using var sp = actual.BuildServiceProvider();

        // Assert
        actual.Should().BeSameAs(services);
        sp.GetService<IAmazonS3>().Should().NotBeNull();
        sp.GetService<IObjectStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumS3_WithCallback_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor>(_ => Substitute.For<ITenantContextAccessor>());

        // Act
        services.AddCompendiumS3(o =>
        {
            o.Bucket = "b1";
            o.ServiceUrl = "http://localhost:9000";
            o.ForcePathStyle = true;
            o.AccessKey = "k";
            o.SecretKey = "s";
        });
        using var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<IObjectStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumS3_NullServices_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumS3(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumS3_NullConfiguration_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act
        var act = () => services.AddCompendiumS3(configuration!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumS3_NullCallback_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumS3((Action<S3Options>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateClient_WithServiceUrl_BuildsCustomEndpoint()
    {
        // Arrange
        var opt = new S3Options
        {
            Bucket = "b",
            ServiceUrl = "http://localhost:9000",
            ForcePathStyle = true,
            AccessKey = "k",
            SecretKey = "s",
        };

        // Act
        using var client = ServiceCollectionExtensions.CreateClient(opt);

        // Assert
        client.Config.ServiceURL.Should().StartWith("http://localhost:9000");
        ((AmazonS3Config)client.Config).ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public void CreateClient_WithRegion_BuildsRegionalEndpoint()
    {
        // Arrange
        var opt = new S3Options
        {
            Bucket = "b",
            Region = "eu-west-1",
            AccessKey = "k",
            SecretKey = "s",
        };

        // Act
        using var client = ServiceCollectionExtensions.CreateClient(opt);

        // Assert
        client.Config.RegionEndpoint.Should().NotBeNull();
        client.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
    }

    [Fact]
    public void CreateClient_WithoutCredentials_UsesDefaultChain()
    {
        // Arrange
        var opt = new S3Options
        {
            Bucket = "b",
            Region = "us-east-1",
        };

        // Act
        var act = () => ServiceCollectionExtensions.CreateClient(opt);

        // Assert
        // Building the client must not throw — the SDK is allowed to fail later
        // when a request is made without credentials, but construction is lazy.
        act.Should().NotThrow();
    }
}
