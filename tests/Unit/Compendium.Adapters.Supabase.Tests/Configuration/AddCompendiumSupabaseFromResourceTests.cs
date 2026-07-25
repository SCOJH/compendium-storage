// -----------------------------------------------------------------------
// <copyright file="AddCompendiumSupabaseFromResourceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.DependencyInjection;
using Compendium.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Adapters.Supabase.Tests.Configuration;

public class AddCompendiumSupabaseFromResourceTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITenantContextAccessor>());
        return services;
    }

    [Fact]
    public void FromResource_WithBucketInConfig_RegistersObjectStore()
    {
        var services = BaseServices();
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:ServiceRoleKey", "svc"),
            ("Resources:db:Bucket", "assets"));

        var returned = services.AddCompendiumSupabaseFromResource(config, "db");
        using var sp = returned.BuildServiceProvider();

        returned.Should().BeSameAs(services);
        sp.GetService<IObjectStore>().Should().NotBeNull();
    }

    [Fact]
    public void FromResource_BucketOverride_TakesPrecedence()
    {
        var services = BaseServices();
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:AnonKey", "anon"),
            ("Resources:db:Bucket", "from-config"));

        // Override should not throw and should register successfully.
        var act = () => services.AddCompendiumSupabaseFromResource(config, "db", bucket: "override");

        act.Should().NotThrow();
    }

    [Fact]
    public void FromResource_NoBucketAnywhere_Throws()
    {
        var services = BaseServices();
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:ServiceRoleKey", "svc"));

        var act = () => services.AddCompendiumSupabaseFromResource(config, "db");

        act.Should().Throw<InvalidOperationException>().WithMessage("*bucket*");
    }

    [Fact]
    public void FromResource_MissingResourceUrl_Throws()
    {
        var services = BaseServices();
        var config = Config(("Resources:db:ServiceRoleKey", "svc"));

        var act = () => services.AddCompendiumSupabaseFromResource(config, "db", bucket: "assets");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromResource_NullArguments_Throw()
    {
        var services = BaseServices();
        var config = Config(("Resources:db:Url", "https://ref.supabase.co"));

        var actServices = () => ((IServiceCollection)null!).AddCompendiumSupabaseFromResource(config, "db");
        var actConfig = () => services.AddCompendiumSupabaseFromResource(null!, "db");
        var actName = () => services.AddCompendiumSupabaseFromResource(config, "  ");

        actServices.Should().Throw<ArgumentNullException>();
        actConfig.Should().Throw<ArgumentNullException>();
        actName.Should().Throw<ArgumentException>();
    }
}
