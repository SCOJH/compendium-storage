// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.DependencyInjection;
using Compendium.Adapters.Supabase.Options;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Supabase.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumSupabase_WithConfiguration_RegistersServices()
    {
        var services = BaseServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compendium:Adapters:Supabase:Url"] = "https://ref.supabase.co",
                ["Compendium:Adapters:Supabase:Bucket"] = "assets",
                ["Compendium:Adapters:Supabase:ServiceRoleKey"] = "service-role",
            })
            .Build();

        var actual = services.AddCompendiumSupabase(configuration);
        using var sp = actual.BuildServiceProvider();

        actual.Should().BeSameAs(services);
        sp.GetService<IObjectStore>().Should().NotBeNull();
        sp.GetService<ISupabaseObjectStoreExtras>().Should().NotBeNull();
        sp.GetService<ISupabaseStorageFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumSupabase_ObjectStoreAndExtras_AreSameInstance()
    {
        var services = BaseServices();

        services.AddCompendiumSupabase(o =>
        {
            o.Url = "https://ref.supabase.co";
            o.Bucket = "assets";
            o.AnonKey = "anon";
        });
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IObjectStore>();
        var extras = sp.GetRequiredService<ISupabaseObjectStoreExtras>();

        store.Should().BeOfType<SupabaseObjectStore>();
        extras.Should().BeSameAs(store);
    }

    [Fact]
    public void AddCompendiumSupabase_WithCallback_RegistersServices()
    {
        var services = BaseServices();

        services.AddCompendiumSupabase(o =>
        {
            o.Url = "https://ref.supabase.co";
            o.Bucket = "assets";
            o.ServiceRoleKey = "service-role";
        });
        using var sp = services.BuildServiceProvider();

        sp.GetService<IObjectStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumSupabase_BothKeysConfigured_FailsOptionsValidation()
    {
        var services = BaseServices();
        services.AddCompendiumSupabase(o =>
        {
            o.Url = "https://ref.supabase.co";
            o.Bucket = "assets";
            o.ServiceRoleKey = "service-role";
            o.AnonKey = "anon";
        });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddCompendiumSupabase_MissingUrl_FailsOptionsValidation()
    {
        var services = BaseServices();
        services.AddCompendiumSupabase(o =>
        {
            o.Bucket = "assets";
            o.ServiceRoleKey = "service-role";
        });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddCompendiumSupabase_NullServices_Throws()
    {
        IServiceCollection? services = null;

        var act = () => services!.AddCompendiumSupabase(_ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumSupabase_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        var act = () => services.AddCompendiumSupabase(configuration!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumSupabase_NullCallback_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddCompendiumSupabase((Action<SupabaseOptions>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITenantContextAccessor>());
        return services;
    }
}
