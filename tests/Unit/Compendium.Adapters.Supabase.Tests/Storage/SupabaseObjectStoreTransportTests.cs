// -----------------------------------------------------------------------
// <copyright file="SupabaseObjectStoreTransportTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.Supabase.Tests.TestSupport;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Supabase.Tests.Storage;

/// <summary>
/// Transport-failure paths : every operation maps a dropped connection to
/// <c>Supabase.{op}.Network</c> / <see cref="ErrorType.Unavailable"/> without throwing.
/// </summary>
public class SupabaseObjectStoreTransportTests
{
    [Fact]
    public async Task GetAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.ObjectUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.GetAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Unavailable);
        actual.Error.Code.Should().Be("Supabase.Get.Network");
    }

    [Fact]
    public async Task DeleteAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Delete, TestStore.ObjectUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.DeleteAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Delete.Network");
    }

    [Fact]
    public async Task ExistsAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.InfoUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.ExistsAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Exists.Network");
    }

    [Fact]
    public async Task ListAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ListUrl()).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync();

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.List.Network");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.SignUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Presign.Network");
    }

    [Fact]
    public async Task CreateSignedUploadAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.UploadSignUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);

        var actual = await store.CreateSignedUploadAsync("k", TimeSpan.FromMinutes(1));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.SignedUpload.Network");
    }

    [Fact]
    public async Task PutAsync_WhenInfoProbeThrows_ReturnsBestEffortInfo()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .Respond(HttpStatusCode.OK, "application/json", """{"Key":"assets/tenant-a/k"}""");
        mockHttp.When(HttpMethod.Get, TestStore.InfoUrl("k")).Throw(new HttpRequestException("down"));
        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1, 2, 3, 4, 5]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Size.Should().Be(5);
        actual.Value.ContentType.Should().Be("text/plain");
    }
}
