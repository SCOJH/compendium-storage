// -----------------------------------------------------------------------
// <copyright file="ObjectStoreContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;

namespace Compendium.Adapters.Supabase.Tests.Contract;

/// <summary>
/// Provider-neutral behavioural contract for <see cref="IObjectStore"/>. Concrete
/// subclasses supply a store implementation ; the same suite runs against the mocked
/// (in-memory storage-api) store here and, in the integration project, against a real
/// <c>supabase/storage-api</c> container.
/// </summary>
public abstract class ObjectStoreContractTests
{
    /// <summary>Builds a fresh, empty store for a single test.</summary>
    /// <returns>The store under test.</returns>
    protected abstract IObjectStore CreateStore();

    [Fact]
    public async Task PutThenGet_RoundTripsBytes()
    {
        var store = CreateStore();
        var payload = Encoding.UTF8.GetBytes("hello contract");

        using (var ms = new MemoryStream(payload))
        {
            (await store.PutAsync("docs/a.txt", ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var get = await store.GetAsync("docs/a.txt");
        get.IsSuccess.Should().BeTrue();
        using var stream = get.Value;
        stream.Info.Key.Should().Be("docs/a.txt");
        using var read = new MemoryStream();
        await stream.Content.CopyToAsync(read);
        read.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Put_ReplacesExistingObject()
    {
        var store = CreateStore();

        using (var v1 = new MemoryStream(Encoding.UTF8.GetBytes("v1")))
        {
            (await store.PutAsync("k", v1, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        using (var v2 = new MemoryStream(Encoding.UTF8.GetBytes("version-2")))
        {
            (await store.PutAsync("k", v2, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var get = await store.GetAsync("k");
        get.IsSuccess.Should().BeTrue();
        using var stream = get.Value;
        using var read = new MemoryStream();
        await stream.Content.CopyToAsync(read);
        Encoding.UTF8.GetString(read.ToArray()).Should().Be("version-2");
    }

    [Fact]
    public async Task Delete_NonExistentKey_IsSuccess()
    {
        var store = CreateStore();

        var deleted = await store.DeleteAsync("never-existed");

        deleted.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_ReflectsPresenceAcrossPutAndDelete()
    {
        var store = CreateStore();

        (await store.ExistsAsync("k")).Value.Should().BeFalse();

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("x")))
        {
            (await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        (await store.ExistsAsync("k")).Value.Should().BeTrue();

        (await store.DeleteAsync("k")).IsSuccess.Should().BeTrue();
        (await store.ExistsAsync("k")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task List_ReturnsObjectsUnderPrefix()
    {
        var store = CreateStore();

        foreach (var name in new[] { "list/a.txt", "list/b.txt" })
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(name));
            (await store.PutAsync(name, ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var list = await store.ListAsync(new ListOptions(Prefix: "list/"));

        list.IsSuccess.Should().BeTrue();
        list.Value.Items.Select(i => i.Key).Should().BeEquivalentTo(["list/a.txt", "list/b.txt"]);
    }

    [Fact]
    public async Task GetPresignedUrl_Get_ReturnsAbsoluteUri()
    {
        var store = CreateStore();

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("x")))
        {
            (await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"))).IsSuccess.Should().BeTrue();
        }

        var presigned = await store.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(5));

        presigned.IsSuccess.Should().BeTrue();
        presigned.Value.IsAbsoluteUri.Should().BeTrue();
    }
}
