// -----------------------------------------------------------------------
// <copyright file="BucketTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Buckets;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Buckets;

public class BucketTests
{
    // ---- CreateBucket ------------------------------------------------------

    [Fact]
    public async Task CreateBucket_OnSuccess_ReturnsBucketEchoingSpec()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/bucket", 200, """{"name":"avatars"}""");

        var spec = new SupabaseBucketSpec
        {
            Id = "avatars",
            Public = true,
            FileSizeLimit = 1024,
            AllowedMimeTypes = ["image/png"],
        };

        var result = await harness.Admin.CreateBucketAsync(harness.ServiceRoleStorage(), spec);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("avatars");
        result.Value.Name.Should().Be("avatars");
        result.Value.Public.Should().BeTrue();
        result.Value.FileSizeLimit.Should().Be(1024);
        result.Value.AllowedMimeTypes.Should().ContainSingle().Which.Should().Be("image/png");
    }

    [Fact]
    public async Task CreateBucket_SendsServiceKeyHeadersAndBody()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/bucket", 200, """{"name":"avatars"}""");

        await harness.Admin.CreateBucketAsync(
            harness.ServiceRoleStorage("svc-key"),
            new SupabaseBucketSpec { Id = "avatars", Public = true });

        var request = harness.Server.LogEntries.Should().ContainSingle().Which.RequestMessage;
        request.Headers!["apikey"].Should().Contain("svc-key");
        request.Headers!["Authorization"].Should().Contain("Bearer svc-key");
        request.Body.Should().Contain("\"id\":\"avatars\"");
        request.Body.Should().Contain("\"public\":true");
    }

    [Fact]
    public async Task CreateBucket_On409_ReturnsConflict()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/bucket", 409, """{"message":"The resource already exists"}""");

        var result = await harness.Admin.CreateBucketAsync(
            harness.ServiceRoleStorage(),
            new SupabaseBucketSpec { Id = "avatars" });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task CreateBucket_AnonKey_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var anon = new SupabaseConnection
        {
            StorageUrl = harness.BaseUrl,
            Credential = new SupabaseCredential.AnonKey("anon"),
        };

        var result = await harness.Admin.CreateBucketAsync(anon, new SupabaseBucketSpec { Id = "avatars" });

        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBucket_NoUrl_ReturnsNotConfigured()
    {
        using var harness = new SupabaseAdminHarness();
        var connection = new SupabaseConnection
        {
            Credential = new SupabaseCredential.ServiceRoleKey("svc"),
        };

        var result = await harness.Admin.CreateBucketAsync(connection, new SupabaseBucketSpec { Id = "avatars" });

        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public async Task CreateBucket_EmptyId_ReturnsValidationFailure()
    {
        using var harness = new SupabaseAdminHarness();

        var result = await harness.Admin.CreateBucketAsync(
            harness.ServiceRoleStorage(), new SupabaseBucketSpec { Id = "" });

        result.Error.Code.Should().Be("Supabase.CreateBucket.InvalidId");
    }

    [Fact]
    public async Task CreateBucket_NullSpec_Throws()
    {
        using var harness = new SupabaseAdminHarness();

        var act = () => harness.Admin.CreateBucketAsync(harness.ServiceRoleStorage(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- ListBuckets -------------------------------------------------------

    [Fact]
    public async Task ListBuckets_OnSuccess_ReturnsMappedBuckets()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", "/bucket", 200,
            """[{"id":"avatars","name":"avatars","public":true,"file_size_limit":2048,"allowed_mime_types":["image/png"],"created_at":"2026-07-24T10:00:00Z"},{"id":"docs","name":"docs","public":false}]""");

        var result = await harness.Admin.ListBucketsAsync(harness.ServiceRoleStorage());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("avatars");
        result.Value[0].Public.Should().BeTrue();
        result.Value[0].FileSizeLimit.Should().Be(2048);
        result.Value[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2026-07-24T10:00:00Z"));
        result.Value[1].Id.Should().Be("docs");
        result.Value[1].Public.Should().BeFalse();
    }

    [Fact]
    public async Task ListBuckets_On401_ReturnsUnauthorized()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", "/bucket", 401);

        var result = await harness.Admin.ListBucketsAsync(harness.ServiceRoleStorage());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task ListBuckets_OnTransportFailure_ReturnsUnavailable()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);
        var connection = new SupabaseConnection
        {
            StorageUrl = new Uri("https://tenant.example.com/storage/v1"),
            Credential = new SupabaseCredential.ServiceRoleKey("svc"),
        };

        var result = await admin.ListBucketsAsync(connection);

        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Supabase.ListBuckets.Network");
    }

    // ---- DeleteBucket ------------------------------------------------------

    [Fact]
    public async Task DeleteBucket_OnSuccess_ReturnsSuccess()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("DELETE", "/bucket/avatars", 200, """{"message":"Successfully deleted"}""");

        var result = await harness.Admin.DeleteBucketAsync(harness.ServiceRoleStorage(), "avatars");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBucket_On404_IsIdempotentSuccess()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("DELETE", "/bucket/avatars", 404);

        var result = await harness.Admin.DeleteBucketAsync(harness.ServiceRoleStorage(), "avatars");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBucket_On403_ReturnsAccessDenied()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("DELETE", "/bucket/avatars", 403, """{"message":"forbidden"}""");

        var result = await harness.Admin.DeleteBucketAsync(harness.ServiceRoleStorage(), "avatars");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("AccessDenied");
    }

    [Fact]
    public async Task DeleteBucket_EmptyId_ReturnsValidationFailure()
    {
        using var harness = new SupabaseAdminHarness();

        var result = await harness.Admin.DeleteBucketAsync(harness.ServiceRoleStorage(), "  ");

        result.Error.Code.Should().Be("Supabase.DeleteBucket.InvalidId");
    }

    [Fact]
    public async Task DeleteBucket_AnonKey_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var anon = new SupabaseConnection
        {
            StorageUrl = harness.BaseUrl,
            Credential = new SupabaseCredential.AnonKey("anon"),
        };

        var result = await harness.Admin.DeleteBucketAsync(anon, "avatars");

        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
    }

    [Fact]
    public async Task DeleteBucket_NullConnection_Throws()
    {
        using var harness = new SupabaseAdminHarness();

        var act = () => harness.Admin.DeleteBucketAsync(null!, "avatars");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
