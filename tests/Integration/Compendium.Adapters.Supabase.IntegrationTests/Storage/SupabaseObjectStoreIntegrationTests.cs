// -----------------------------------------------------------------------
// <copyright file="SupabaseObjectStoreIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Text;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.Supabase.DependencyInjection;
using Compendium.Adapters.Supabase.IntegrationTests.Fixtures;
using Compendium.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Compendium.Adapters.Supabase.IntegrationTests.Storage;

/// <summary>
/// End-to-end roundtrip tests against a real <c>supabase/storage-api</c> container.
/// Skipped automatically when Docker is unavailable.
/// </summary>
public sealed class SupabaseObjectStoreIntegrationTests : IClassFixture<SupabaseStorageFixture>, IDisposable
{
    private readonly SupabaseStorageFixture _fixture;
    private readonly List<ServiceProvider> _providers = [];

    public SupabaseObjectStoreIntegrationTests(SupabaseStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    [RequiresDockerFact]
    public async Task PutGetExistsListDelete_Roundtrip_SmallPayload()
    {
        var store = CreateStore("tenant-a");
        const string key = "invoices/2026/inv-001.txt";
        var payload = Encoding.UTF8.GetBytes("hello compendium supabase");

        using (var ms = new MemoryStream(payload))
        {
            var put = await store.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"));
            put.IsSuccess.Should().BeTrue();
            put.Value.Key.Should().Be(key);
        }

        var existsAfterPut = await store.ExistsAsync(key);
        existsAfterPut.IsSuccess.Should().BeTrue(Because(existsAfterPut));
        existsAfterPut.Value.Should().BeTrue();

        var get = await store.GetAsync(key);
        get.IsSuccess.Should().BeTrue(Because(get));
        using (var stream = get.Value)
        using (var read = new MemoryStream())
        {
            stream.Info.Key.Should().Be(key);
            await stream.Content.CopyToAsync(read);
            read.ToArray().Should().Equal(payload);
        }

        var list = await store.ListAsync(new ListOptions(Prefix: "invoices/2026/"));
        list.IsSuccess.Should().BeTrue(Because(list));
        list.Value.Items.Should().Contain(o => o.Key == key);

        (await store.DeleteAsync(key)).IsSuccess.Should().BeTrue();
        var existsAfterDelete = await store.ExistsAsync(key);
        existsAfterDelete.IsSuccess.Should().BeTrue(Because(existsAfterDelete));
        existsAfterDelete.Value.Should().BeFalse();
    }

    [RequiresDockerFact]
    public async Task TenantIsolation_TenantB_CannotSeeTenantAObjects()
    {
        var aStore = CreateStore("tenant-a");
        var bStore = CreateStore("tenant-b");
        const string key = "private.txt";

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("secret")))
        {
            (await aStore.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var bExists = await bStore.ExistsAsync(key);
        bExists.IsSuccess.Should().BeTrue(Because(bExists));
        bExists.Value.Should().BeFalse();

        var bList = await bStore.ListAsync();
        bList.IsSuccess.Should().BeTrue(Because(bList));
        bList.Value.Items.Should().BeEmpty();

        var get = await bStore.GetAsync(key);
        get.IsFailure.Should().BeTrue();
        get.Error.Type.Should().Be(ErrorType.NotFound);
    }

    private static string Because(Result result) =>
        result.IsFailure ? $"expected success but got {result.Error.Code}: {result.Error.Message}" : string.Empty;

    [RequiresDockerFact]
    public async Task PresignedGet_DownloadsObject()
    {
        var store = CreateStore("tenant-a");
        const string key = "presign/sample.txt";
        var payload = Encoding.UTF8.GetBytes("presigned payload");

        using (var ms = new MemoryStream(payload))
        {
            (await store.PutAsync(key, ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var presigned = await store.GetPresignedUrlAsync(key, PresignedAction.Get, TimeSpan.FromMinutes(5));
        presigned.IsSuccess.Should().BeTrue();

        using var http = new HttpClient();
        using var response = await http.GetAsync(presigned.Value);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(payload);
    }

    private IObjectStore CreateStore(string tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor>(new StaticTenantContextAccessor(tenantId));
        services.AddCompendiumSupabase(o =>
        {
            o.Url = "http://localhost";           // unused — StorageUrl override targets the container directly
            o.StorageUrl = _fixture.StorageUrl;
            o.ServiceRoleKey = _fixture.ServiceRoleKey;
            o.Bucket = _fixture.Bucket;
        });

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IObjectStore>();
    }
}
