// -----------------------------------------------------------------------
// <copyright file="SupabaseResourceBindingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Configuration;
using Compendium.Adapters.Supabase.Connections;
using Microsoft.Extensions.Configuration;

namespace Compendium.Adapters.Supabase.Tests.Configuration;

public class SupabaseResourceBindingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

    [Fact]
    public void FromConfiguration_WithServiceRoleKey_BindsConnectionAndFields()
    {
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:ServiceRoleKey", "svc"),
            ("Resources:db:DbConnectionString", "postgres://u:p@h/d"),
            ("Resources:db:Bucket", "assets"));

        var result = SupabaseResourceBinding.FromConfiguration(config, "db");

        result.IsSuccess.Should().BeTrue();
        var binding = result.Value;
        binding.Url.Should().Be("https://ref.supabase.co");
        binding.ServiceRoleKey.Should().Be("svc");
        binding.DbConnectionString.Should().Be("postgres://u:p@h/d");
        binding.Bucket.Should().Be("assets");
        binding.Connection.ProjectUrl.Should().Be(new Uri("https://ref.supabase.co"));
        binding.Connection.Credential.Should().BeOfType<SupabaseCredential.ServiceRoleKey>();
    }

    [Fact]
    public void FromConfiguration_PrefersServiceRoleKeyOverAnonKey()
    {
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:ServiceRoleKey", "svc"),
            ("Resources:db:AnonKey", "anon"));

        var binding = SupabaseResourceBinding.FromConfiguration(config, "db").Value;

        binding.Connection.Credential.Should().BeOfType<SupabaseCredential.ServiceRoleKey>();
    }

    [Fact]
    public void FromConfiguration_WithAnonKeyOnly_UsesAnonKey()
    {
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:AnonKey", "anon"));

        var binding = SupabaseResourceBinding.FromConfiguration(config, "db").Value;

        binding.Connection.Credential.Should().BeOfType<SupabaseCredential.AnonKey>();
    }

    [Fact]
    public void FromConfiguration_MissingUrl_ReturnsNotConfigured()
    {
        var config = Config(("Resources:db:ServiceRoleKey", "svc"));

        var result = SupabaseResourceBinding.FromConfiguration(config, "db");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void FromConfiguration_InvalidUrl_ReturnsNotConfigured()
    {
        var config = Config(
            ("Resources:db:Url", "not-a-url"),
            ("Resources:db:ServiceRoleKey", "svc"));

        var result = SupabaseResourceBinding.FromConfiguration(config, "db");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void FromConfiguration_MissingKeys_ReturnsNotConfigured()
    {
        var config = Config(("Resources:db:Url", "https://ref.supabase.co"));

        var result = SupabaseResourceBinding.FromConfiguration(config, "db");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void FromConfiguration_BlankResourceName_ReturnsNotConfigured()
    {
        var result = SupabaseResourceBinding.FromConfiguration(Config(), "  ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void FromConfiguration_NullConfiguration_Throws()
    {
        var act = () => SupabaseResourceBinding.FromConfiguration(null!, "db");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToString_RedactsKeyMaterial()
    {
        var config = Config(
            ("Resources:db:Url", "https://ref.supabase.co"),
            ("Resources:db:ServiceRoleKey", "super-secret"),
            ("Resources:db:AnonKey", "anon-secret"),
            ("Resources:db:DbConnectionString", "postgres://u:pw@h/d"),
            ("Resources:db:Bucket", "assets"));

        var text = SupabaseResourceBinding.FromConfiguration(config, "db").Value.ToString();

        text.Should().NotContain("super-secret");
        text.Should().NotContain("anon-secret");
        text.Should().NotContain("postgres://");
        text.Should().Contain("ServiceRoleKey=***");
        text.Should().Contain("Bucket=assets");
    }
}
