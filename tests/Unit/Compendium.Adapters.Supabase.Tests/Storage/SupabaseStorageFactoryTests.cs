// -----------------------------------------------------------------------
// <copyright file="SupabaseStorageFactoryTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Adapters.Supabase.Tests.TestSupport;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Supabase.Tests.Storage;

public class SupabaseStorageFactoryTests
{
    private const string Bucket = "assets";
    private const string Tenant = "tenant-a";

    [Fact]
    public void Create_NullConnection_Throws()
    {
        var factory = BuildFactory(new MockHttpMessageHandler());

        var act = () => factory.Create(null!, Bucket);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_EmptyBucket_ReturnsNotConfigured()
    {
        var factory = BuildFactory(new MockHttpMessageHandler());

        var result = factory.Create(ServiceRoleConnection(), string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void Create_ManagementTokenCredential_ReturnsNotConfigured()
    {
        var factory = BuildFactory(new MockHttpMessageHandler());

        var result = factory.Create(
            new SupabaseConnection
            {
                ProjectUrl = new Uri("https://ref.supabase.co"),
                Credential = new SupabaseCredential.ManagementToken("pat"),
            },
            Bucket);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public void Create_NoUrl_ReturnsNotConfigured()
    {
        var factory = BuildFactory(new MockHttpMessageHandler());

        var result = factory.Create(
            new SupabaseConnection { Credential = new SupabaseCredential.ServiceRoleKey("k") },
            Bucket);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public async Task Create_ValidServiceRoleConnection_ReturnsUsableStore()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Delete, $"https://ref.supabase.co/storage/v1/object/{Bucket}/{Tenant}/k")
            .Respond(HttpStatusCode.OK);
        var factory = BuildFactory(mockHttp);

        var created = factory.Create(ServiceRoleConnection(), Bucket);
        created.IsSuccess.Should().BeTrue();

        var deleted = await created.Value.DeleteAsync("k");

        deleted.IsSuccess.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Create_StorageUrlOverride_TargetsOverrideEndpoint()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Delete, $"http://localhost:5000/storage/v1/object/{Bucket}/{Tenant}/k")
            .Respond(HttpStatusCode.OK);
        var factory = BuildFactory(mockHttp);

        var created = factory.Create(
            new SupabaseConnection
            {
                ProjectUrl = new Uri("https://ref.supabase.co"),
                StorageUrl = new Uri("http://localhost:5000/storage/v1"),
                Credential = new SupabaseCredential.AnonKey("anon-key"),
            },
            Bucket);
        created.IsSuccess.Should().BeTrue();

        var deleted = await created.Value.DeleteAsync("k");

        deleted.IsSuccess.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    private static SupabaseConnection ServiceRoleConnection() => new()
    {
        ProjectUrl = new Uri("https://ref.supabase.co"),
        Credential = new SupabaseCredential.ServiceRoleKey("service-role-key"),
    };

    private static SupabaseStorageFactory BuildFactory(MockHttpMessageHandler mockHttp)
    {
        ITenantContextAccessor tenant = new StaticTenantContextAccessor(Tenant);
        return new SupabaseStorageFactory(new TestHttpClientFactory(mockHttp), tenant, NullLoggerFactory.Instance);
    }
}
