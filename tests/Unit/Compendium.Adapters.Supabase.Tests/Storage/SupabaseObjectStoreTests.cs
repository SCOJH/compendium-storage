// -----------------------------------------------------------------------
// <copyright file="SupabaseObjectStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Compendium.Abstractions.Storage.Models;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Adapters.Supabase.Tests.TestSupport;
using Compendium.Multitenancy;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Supabase.Tests.Storage;

public class SupabaseObjectStoreTests
{
    private static readonly string InfoJson = """
        {"name":"tenant-a/invoices/inv-1.pdf","updated_at":"2026-05-17T12:00:00+00:00",
         "id":"row-1",
         "metadata":{"size":5,"mimetype":"application/pdf","eTag":"\"etag-1\"","lastModified":"2026-05-17T12:00:00+00:00"}}
        """;

    // -------------------------- Constructor --------------------------

    [Fact]
    public void Constructor_NullHttpClientFactory_Throws()
    {
        var act = () => new SupabaseObjectStore(
            null!, Ctx(), SupabaseCapabilities.ForProjectPlane(), Tenant(), NullLogger<SupabaseObjectStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        var act = () => new SupabaseObjectStore(
            new TestHttpClientFactory(new MockHttpMessageHandler()), null!, SupabaseCapabilities.ForProjectPlane(), Tenant(), NullLogger<SupabaseObjectStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCapabilities_Throws()
    {
        var act = () => new SupabaseObjectStore(
            new TestHttpClientFactory(new MockHttpMessageHandler()), Ctx(), null!, Tenant(), NullLogger<SupabaseObjectStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTenantAccessor_Throws()
    {
        var act = () => new SupabaseObjectStore(
            new TestHttpClientFactory(new MockHttpMessageHandler()), Ctx(), SupabaseCapabilities.ForProjectPlane(), null!, NullLogger<SupabaseObjectStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new SupabaseObjectStore(
            new TestHttpClientFactory(new MockHttpMessageHandler()), Ctx(), SupabaseCapabilities.ForProjectPlane(), Tenant(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------- Tenant resolution --------------------------

    [Fact]
    public async Task PutAsync_WithoutTenant_ReturnsForbidden()
    {
        var store = TestStore.Create(new MockHttpMessageHandler(), new StaticTenantContextAccessor(null));
        using var ms = new MemoryStream([1, 2, 3]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
        actual.Error.Code.Should().Be("Supabase.NoTenant");
    }

    [Fact]
    public async Task PutAsync_WithMalformedTenant_ReturnsForbidden()
    {
        var store = TestStore.Create(new MockHttpMessageHandler(), new StaticTenantContextAccessor("tenant with spaces"));
        using var ms = new MemoryStream([1, 2, 3]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.InvalidTenant");
    }

    // -------------------------- PutAsync --------------------------

    [Fact]
    public async Task PutAsync_HappyPath_UploadsWithTenantPrefix_AndReturnsDescribedInfo()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.ObjectUrl("invoices/inv-1.pdf"))
            .WithHeaders("apikey", TestStore.ApiKey)
            .WithHeaders("Authorization", $"Bearer {TestStore.ApiKey}")
            .WithHeaders("x-upsert", "true")
            .Respond(HttpStatusCode.OK, "application/json", """{"Key":"assets/tenant-a/invoices/inv-1.pdf"}""");
        mockHttp.Expect(HttpMethod.Get, TestStore.InfoUrl("invoices/inv-1.pdf"))
            .Respond("application/json", InfoJson);

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var actual = await store.PutAsync("invoices/inv-1.pdf", ms, new ObjectMetadata(ContentType: "application/pdf"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Key.Should().Be("invoices/inv-1.pdf");
        actual.Value.Size.Should().Be(5);
        actual.Value.ETag.Should().Be("etag-1");
        actual.Value.ContentType.Should().Be("application/pdf");
        actual.Value.LastModified.Should().Be(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PutAsync_SetsContentTypeAndCacheControlOnContent()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .With(req => req.Content!.Headers.ContentType!.MediaType == "text/plain"
                && req.Headers.TryGetValues("cache-control", out var cc) && cc.First() == "max-age=3600")
            .Respond(HttpStatusCode.OK, "application/json", """{"Key":"assets/tenant-a/k"}""");
        mockHttp.Expect(HttpMethod.Get, TestStore.InfoUrl("k")).Respond("application/json", InfoJson);

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1, 2, 3]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain", CacheControl: "max-age=3600"));

        actual.IsSuccess.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PutAsync_NullMetadata_UsesOctetStream_AndStillUploads()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .With(req => req.Content!.Headers.ContentType!.MediaType == "application/octet-stream")
            .Respond(HttpStatusCode.OK, "application/json", """{"Key":"assets/tenant-a/k"}""");
        mockHttp.Expect(HttpMethod.Get, TestStore.InfoUrl("k")).Respond("application/json", InfoJson);

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1, 2, 3]);

        var actual = await store.PutAsync("k", ms);

        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_WhenInfoUnavailable_ReturnsBestEffortInfoFromUpload()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .Respond(HttpStatusCode.OK, "application/json", """{"Key":"assets/tenant-a/k"}""");
        mockHttp.When(HttpMethod.Get, TestStore.InfoUrl("k")).Respond(HttpStatusCode.NotFound);

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1, 2, 3, 4]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Key.Should().Be("k");
        actual.Value.Size.Should().Be(4); // falls back to the uploaded stream length
        actual.Value.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task PutAsync_UploadFails_MapsToStorageError()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .Respond(HttpStatusCode.Forbidden, "application/json", """{"code":"Forbidden","message":"nope"}""");

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
        actual.Error.Code.Should().Be("Storage.AccessDenied");
    }

    [Fact]
    public async Task PutAsync_InvalidKey_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());
        using var ms = new MemoryStream([1]);

        var actual = await store.PutAsync("../escape", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
        actual.Error.Code.Should().Be("Supabase.Put.InvalidKey");
    }

    [Fact]
    public async Task PutAsync_NetworkFailure_MapsToUnavailable()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ObjectUrl("k"))
            .Throw(new HttpRequestException("connection refused"));

        var store = TestStore.Create(mockHttp);
        using var ms = new MemoryStream([1]);

        var actual = await store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Unavailable);
        actual.Error.Code.Should().Be("Supabase.Put.Network");
    }

    [Fact]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());
        using var ms = new MemoryStream([1]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.PutAsync("k", ms, new ObjectMetadata(ContentType: "text/plain"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------- GetAsync --------------------------

    [Fact]
    public async Task GetAsync_HappyPath_ReturnsObjectStream()
    {
        var payload = Encoding.UTF8.GetBytes("hello compendium");
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.ObjectUrl("k.bin")).Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero);
            response.Headers.ETag = new EntityTagHeaderValue("\"etag-get\"");
            return response;
        });

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetAsync("k.bin");

        actual.IsSuccess.Should().BeTrue();
        using var stream = actual.Value;
        stream.Info.Key.Should().Be("k.bin");
        stream.Info.Size.Should().Be(payload.Length);
        stream.Info.ETag.Should().Be("etag-get");
        stream.Info.ContentType.Should().Be("application/octet-stream");
        using var read = new MemoryStream();
        await stream.Content.CopyToAsync(read);
        read.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task GetAsync_Dispose_AlsoDisposesUnderlyingStream()
    {
        var tracking = new TrackingStream(Encoding.UTF8.GetBytes("x"));
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.ObjectUrl("k"))
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(tracking) });

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetAsync("k");
        actual.Value.Dispose();

        tracking.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsStorageNotFound()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.ObjectUrl("k"))
            .Respond(HttpStatusCode.NotFound, "application/json", """{"code":"NoSuchKey","message":"missing"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.NotFound);
        actual.Error.Code.Should().Be("Storage.NotFound");
    }

    [Fact]
    public async Task GetAsync_InvalidKey_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = await store.GetAsync("..");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Get.InvalidKey");
    }

    [Fact]
    public async Task GetAsync_NoTenant_ReturnsForbidden()
    {
        var store = TestStore.Create(new MockHttpMessageHandler(), new StaticTenantContextAccessor(null));

        var actual = await store.GetAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.NoTenant");
    }

    [Fact]
    public async Task GetAsync_AlreadyCancelledToken_Throws()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.GetAsync("k", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------- DeleteAsync --------------------------

    [Fact]
    public async Task DeleteAsync_HappyPath_ReturnsSuccess()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Delete, TestStore.ObjectUrl("k")).Respond(HttpStatusCode.OK);

        var store = TestStore.Create(mockHttp);

        var actual = await store.DeleteAsync("k");

        actual.IsSuccess.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_IsIdempotentSuccess()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Delete, TestStore.ObjectUrl("k")).Respond(HttpStatusCode.NotFound);

        var store = TestStore.Create(mockHttp);

        var actual = await store.DeleteAsync("k");

        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ServerError_ReturnsFailure()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Delete, TestStore.ObjectUrl("k"))
            .Respond(HttpStatusCode.InternalServerError, "application/json", """{"code":"InternalError","message":"boom"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.DeleteAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Delete.InternalError");
    }

    [Fact]
    public async Task DeleteAsync_InvalidKey_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = await store.DeleteAsync("");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
    }

    // -------------------------- ExistsAsync --------------------------

    [Fact]
    public async Task ExistsAsync_WhenObjectPresent_ReturnsTrue()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Get, TestStore.InfoUrl("k")).Respond("application/json", InfoJson);

        var store = TestStore.Create(mockHttp);

        var actual = await store.ExistsAsync("k");

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().BeTrue();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectMissing_ReturnsFalse()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.InfoUrl("k")).Respond(HttpStatusCode.NotFound);

        var store = TestStore.Create(mockHttp);

        var actual = await store.ExistsAsync("k");

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ServerError_ReturnsFailure()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, TestStore.InfoUrl("k"))
            .Respond(HttpStatusCode.InternalServerError, "application/json", """{"code":"InternalError","message":"boom"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.ExistsAsync("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("Supabase.Exists.");
    }

    // -------------------------- ListAsync --------------------------

    [Fact]
    public async Task ListAsync_NoTenant_ReturnsForbidden()
    {
        var store = TestStore.Create(new MockHttpMessageHandler(), new StaticTenantContextAccessor(null));

        var actual = await store.ListAsync();

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.NoTenant");
    }

    [Fact]
    public async Task ListAsync_ScopesPrefixToTenant_AndStripsPrefixFromResults()
    {
        var listJson = """
            [
              {"name":"a.txt","id":"1","metadata":{"size":100,"mimetype":"text/plain","eTag":"\"e-a\"","lastModified":"2026-05-17T12:00:00+00:00"}},
              {"name":"reports","id":null,"metadata":null},
              {"name":"b.pdf","id":"2","metadata":{"size":200,"mimetype":"application/pdf","eTag":"\"e-b\""}}
            ]
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.ListUrl())
            .With(req => req.Content!.ReadAsStringAsync().Result.Contains("\"prefix\":\"tenant-a/\""))
            .Respond("application/json", listJson);

        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync();

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Items.Should().HaveCount(2); // folder pseudo-entry "reports" is filtered out
        actual.Value.Items[0].Key.Should().Be("a.txt");
        actual.Value.Items[0].Size.Should().Be(100);
        actual.Value.Items[0].ETag.Should().Be("e-a");
        actual.Value.Items[1].Key.Should().Be("b.pdf");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ListAsync_SubPrefix_ComposesTenantPrefixAndStripsResults()
    {
        var listJson = """
            [{"name":"inv-1.pdf","id":"1","metadata":{"size":10,"mimetype":"application/pdf","eTag":"\"e\""}}]
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ListUrl())
            .With(req => req.Content!.ReadAsStringAsync().Result.Contains("\"prefix\":\"tenant-a/invoices/\""))
            .Respond("application/json", listJson);

        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync(new ListOptions(Prefix: "invoices/"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Items.Should().ContainSingle();
        actual.Value.Items[0].Key.Should().Be("invoices/inv-1.pdf");
    }

    [Fact]
    public async Task ListAsync_WhenFullPage_ReturnsContinuationTokenEncodingNextOffset()
    {
        var page = """
            [{"name":"a","id":"1","metadata":{"size":1,"eTag":"\"e\""}},
             {"name":"b","id":"2","metadata":{"size":1,"eTag":"\"e\""}}]
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ListUrl())
            .With(req => req.Content!.ReadAsStringAsync().Result.Contains("\"offset\":0"))
            .Respond("application/json", page);

        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync(new ListOptions(MaxKeys: 2));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.NextContinuationToken.Should().NotBeNullOrEmpty();
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(actual.Value.NextContinuationToken!));
        decoded.Should().Be("2");
    }

    [Fact]
    public async Task ListAsync_WithContinuationToken_SendsDecodedOffset()
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("2"));
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.ListUrl())
            .With(req => req.Content!.ReadAsStringAsync().Result.Contains("\"offset\":2"))
            .Respond("application/json", "[]");

        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync(new ListOptions(MaxKeys: 2, ContinuationToken: token));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Items.Should().BeEmpty();
        actual.Value.NextContinuationToken.Should().BeNull();
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ListAsync_ServerError_ReturnsFailure()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.ListUrl())
            .Respond(HttpStatusCode.InternalServerError, "application/json", """{"code":"InternalError","message":"boom"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.ListAsync();

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().StartWith("Supabase.List.");
    }

    // -------------------------- GetPresignedUrlAsync --------------------------

    [Fact]
    public async Task GetPresignedUrlAsync_Get_ComposesAbsoluteSignedUrl()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.SignUrl("k"))
            .With(req => req.Content!.ReadAsStringAsync().Result.Contains("\"expiresIn\":300"))
            .Respond("application/json", """{"signedURL":"/object/sign/assets/tenant-a/k?token=jwt"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(5));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.AbsoluteUri.Should().Be("https://ref.supabase.co/storage/v1/object/sign/assets/tenant-a/k?token=jwt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Put_ReturnsSignedUploadUrl()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.UploadSignUrl("k"))
            .Respond("application/json", """{"url":"/object/upload/sign/assets/tenant-a/k?token=jwt","token":"jwt"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetPresignedUrlAsync("k", PresignedAction.Put, TimeSpan.FromMinutes(5));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.AbsoluteUri.Should().Be("https://ref.supabase.co/storage/v1/object/upload/sign/assets/tenant-a/k?token=jwt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPresignedUrlAsync_NonPositiveExpiry_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = await store.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.Zero);

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Validation);
        actual.Error.Code.Should().Be("Supabase.Presign.InvalidExpiry");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_InvalidKey_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = await store.GetPresignedUrlAsync("..", PresignedAction.Get, TimeSpan.FromMinutes(1));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.Presign.InvalidKey");
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ProviderError_Maps()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.SignUrl("k"))
            .Respond(HttpStatusCode.Forbidden, "application/json", """{"code":"Forbidden","message":"nope"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.GetPresignedUrlAsync("k", PresignedAction.Get, TimeSpan.FromMinutes(1));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // -------------------------- Extras : public URL --------------------------

    [Fact]
    public void GetPublicUrl_ComposesTenantScopedPublicUrl()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = store.GetPublicUrl("img/a.png");

        actual.IsSuccess.Should().BeTrue();
        actual.Value.AbsoluteUri.Should().Be("https://ref.supabase.co/storage/v1/object/public/assets/tenant-a/img/a.png");
    }

    [Fact]
    public void GetPublicUrl_WithTransform_UsesRenderPathAndQuery()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = store.GetPublicUrl("img/a.png", new ImageTransform(Width: 100, Height: 50, Resize: "cover"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.AbsoluteUri.Should().Be(
            "https://ref.supabase.co/storage/v1/render/image/public/assets/tenant-a/img/a.png?width=100&height=50&resize=cover");
    }

    [Fact]
    public void GetPublicUrl_InvalidKey_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = store.GetPublicUrl("..");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.PublicUrl.InvalidKey");
    }

    [Fact]
    public void GetPublicUrl_NoTenant_ReturnsForbidden()
    {
        var store = TestStore.Create(new MockHttpMessageHandler(), new StaticTenantContextAccessor(null));

        var actual = store.GetPublicUrl("k");

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.NoTenant");
    }

    // -------------------------- Extras : signed upload --------------------------

    [Fact]
    public async Task CreateSignedUploadAsync_ReturnsSignedUpload()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, TestStore.UploadSignUrl("k"))
            .Respond("application/json", """{"url":"/object/upload/sign/assets/tenant-a/k?token=jwt","token":"jwt"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.CreateSignedUploadAsync("k", TimeSpan.FromMinutes(10));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Key.Should().Be("k");
        actual.Value.Token.Should().Be("jwt");
        actual.Value.UploadUrl.AbsoluteUri.Should().Be("https://ref.supabase.co/storage/v1/object/upload/sign/assets/tenant-a/k?token=jwt");
        actual.Value.ToString().Should().NotContain("jwt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task CreateSignedUploadAsync_NonPositiveExpiry_ReturnsValidationError()
    {
        var store = TestStore.Create(new MockHttpMessageHandler());

        var actual = await store.CreateSignedUploadAsync("k", TimeSpan.Zero);

        actual.IsFailure.Should().BeTrue();
        actual.Error.Code.Should().Be("Supabase.SignedUpload.InvalidExpiry");
    }

    [Fact]
    public async Task CreateSignedUploadAsync_ProviderError_Maps()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, TestStore.UploadSignUrl("k"))
            .Respond(HttpStatusCode.NotFound, "application/json", """{"code":"NoSuchKey","message":"missing"}""");

        var store = TestStore.Create(mockHttp);

        var actual = await store.CreateSignedUploadAsync("k", TimeSpan.FromMinutes(10));

        actual.IsFailure.Should().BeTrue();
        actual.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // -------------------------- Helpers --------------------------

    private static SupabaseStorageContext Ctx() => new()
    {
        StorageBaseUrl = TestStore.StorageBaseUrl,
        ApiKey = TestStore.ApiKey,
        Bucket = TestStore.Bucket,
        DefaultPresignedUrlExpiry = TimeSpan.FromMinutes(15),
    };

    private static ITenantContextAccessor Tenant() => new StaticTenantContextAccessor(TestStore.TenantId);

    private sealed class TrackingStream : MemoryStream
    {
        public TrackingStream(byte[] buffer)
            : base(buffer)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
