// -----------------------------------------------------------------------
// <copyright file="RedactionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Management.Projects;

namespace Compendium.Adapters.Supabase.Management.Tests.Models;

public class RedactionTests
{
    [Fact]
    public void SupabaseProjectSpec_ToString_RedactsDbPassword()
    {
        var spec = new SupabaseProjectSpec
        {
            Name = "n",
            OrganizationSlug = "org",
            Region = "eu-central-1",
            DbPassword = "super-secret-password",
            Plan = "pro",
        };

        var text = spec.ToString();

        text.Should().NotContain("super-secret-password");
        text.Should().Contain("DbPassword=***");
        text.Should().Contain("Name=n");
        text.Should().Contain("Plan=pro");
    }

    [Fact]
    public void SupabaseProjectSpec_ToString_ShowsDefaultPlanWhenNull()
    {
        var spec = new SupabaseProjectSpec
        {
            Name = "n",
            OrganizationSlug = "org",
            Region = "r",
            DbPassword = "p",
        };

        spec.ToString().Should().Contain("Plan=(default)");
    }

    [Fact]
    public void SupabaseProjectKeys_ToString_RedactsAllKeyMaterial()
    {
        var keys = new SupabaseProjectKeys
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            AnonKey = "anon-secret",
            ServiceRoleKey = "service-secret",
            DbConnectionString = "postgres://user:pw@host/db",
        };

        var text = keys.ToString();

        text.Should().NotContain("anon-secret");
        text.Should().NotContain("service-secret");
        text.Should().NotContain("postgres://");
        text.Should().Contain("AnonKey=***");
        text.Should().Contain("ServiceRoleKey=***");
        text.Should().Contain("DbConnectionString=***");
        text.Should().Contain("https://ref.supabase.co");
    }

    [Fact]
    public void SupabaseProjectKeys_ToString_ShowsNoneWhenDbConnectionStringNull()
    {
        var keys = new SupabaseProjectKeys
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            AnonKey = "a",
            ServiceRoleKey = "s",
        };

        keys.ToString().Should().Contain("DbConnectionString=(none)");
    }
}
