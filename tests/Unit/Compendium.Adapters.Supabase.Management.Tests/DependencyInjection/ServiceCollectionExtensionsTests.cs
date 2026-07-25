// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.DependencyInjection;
using Compendium.Adapters.Supabase.Management.DependencyInjection;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Compendium.Abstractions.Storage;
using Compendium.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Adapters.Supabase.Management.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumSupabaseManagement_RegistersSingletonAdmin()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var returned = services.AddCompendiumSupabaseManagement();
        using var sp = services.BuildServiceProvider();

        returned.Should().BeSameAs(services);
        var first = sp.GetService<ISupabaseAdmin>();
        var second = sp.GetService<ISupabaseAdmin>();
        first.Should().NotBeNull();
        second.Should().BeSameAs(first, "the admin is a stateless singleton");
    }

    [Fact]
    public void AddCompendiumSupabaseManagement_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCompendiumSupabaseManagement();
        services.AddCompendiumSupabaseManagement();
        using var sp = services.BuildServiceProvider();

        sp.GetServices<ISupabaseAdmin>().Should().ContainSingle();
    }

    [Fact]
    public void AddCompendiumSupabaseManagement_CoexistsWithRuntimeAdapter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor>(new StubTenantContextAccessor());

        services.AddCompendiumSupabase(o =>
        {
            o.Url = "https://ref.supabase.co";
            o.Bucket = "assets";
            o.ServiceRoleKey = "svc";
        });
        services.AddCompendiumSupabaseManagement();
        using var sp = services.BuildServiceProvider();

        sp.GetService<IObjectStore>().Should().NotBeNull();
        sp.GetService<ISupabaseAdmin>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumSupabaseManagement_NullServices_Throws()
    {
        IServiceCollection? services = null;

        var act = () => services!.AddCompendiumSupabaseManagement();

        act.Should().Throw<ArgumentNullException>();
    }
}
