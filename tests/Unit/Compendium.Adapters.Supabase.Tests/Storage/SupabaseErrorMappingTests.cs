// -----------------------------------------------------------------------
// <copyright file="SupabaseErrorMappingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using Compendium.Adapters.Supabase.Storage;

namespace Compendium.Adapters.Supabase.Tests.Storage;

public class SupabaseErrorMappingTests
{
    private const string Bucket = "assets";

    [Fact]
    public async Task Map_404_WithoutBucketMention_RoutesToStorageNotFound()
    {
        using var response = Json(HttpStatusCode.NotFound, """{"code":"NoSuchKey","message":"Object not found"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("Storage.NotFound");
    }

    [Fact]
    public async Task Map_404_WithBucketMention_RoutesToInvalidBucket()
    {
        using var response = Json(HttpStatusCode.NotFound, """{"code":"NoSuchBucket","message":"Bucket not found"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("Storage.InvalidBucket");
        error.Message.Should().Contain(Bucket);
    }

    [Fact]
    public async Task Map_403_RoutesToAccessDenied()
    {
        using var response = Json(HttpStatusCode.Forbidden, """{"code":"Forbidden","message":"nope"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Type.Should().Be(ErrorType.Forbidden);
        error.Code.Should().Be("Storage.AccessDenied");
    }

    [Fact]
    public async Task Map_409_RoutesToConflictExists()
    {
        using var response = Json(HttpStatusCode.Conflict, """{"code":"Duplicate","message":"already exists"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Put", "k", Bucket);

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("Storage.ConflictExists");
    }

    [Fact]
    public async Task Map_429_RoutesToThrottled_WithRetryAfter()
    {
        using var response = Json(HttpStatusCode.TooManyRequests, """{"code":"TooManyRequests","message":"slow down"}""");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Type.Should().Be(ErrorType.TooManyRequests);
        error.Code.Should().Be("Storage.Throttled");
        error.Message.Should().Contain("30");
    }

    [Fact]
    public async Task Map_413_RoutesToContentTooLarge()
    {
        using var response = Json(HttpStatusCode.RequestEntityTooLarge, """{"code":"PayloadTooLarge","message":"too big"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Put", "k", Bucket);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("Storage.ContentTooLarge");
    }

    [Fact]
    public async Task Map_401_RoutesToUnauthorized_WithSupabasePrefixedCode()
    {
        using var response = Json(HttpStatusCode.Unauthorized, """{"code":"InvalidJWT","message":"bad key"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("Supabase.Get.InvalidJWT");
    }

    [Fact]
    public async Task Map_400_NonBucket_RoutesToValidation_WithSupabaseCode()
    {
        using var response = Json(HttpStatusCode.BadRequest, """{"code":"InvalidRequest","message":"bad request"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "List", "prefix", Bucket);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("Supabase.List.InvalidRequest");
    }

    [Fact]
    public async Task Map_500_RoutesToUnavailable()
    {
        using var response = Json(HttpStatusCode.InternalServerError, """{"code":"InternalError","message":"boom"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Put", "k", Bucket);

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("Supabase.Put.InternalError");
    }

    [Fact]
    public async Task Map_LegacyBodyShape_ParsesErrorField()
    {
        using var response = Json(
            HttpStatusCode.InternalServerError,
            """{"statusCode":"500","error":"InternalError","message":"boom"}""");

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Code.Should().Be("Supabase.Get.InternalError");
        error.Message.Should().Contain("boom");
    }

    [Fact]
    public async Task Map_NonJsonBody_FallsBackToUnknownCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>gateway error</html>"),
        };

        var error = await SupabaseErrorMapping.MapAsync(response, "Get", "k", Bucket);

        error.Code.Should().Be("Supabase.Get.Unknown");
        error.Message.Should().Contain("gateway error");
    }

    [Fact]
    public void MapException_RoutesToUnavailableNetworkError()
    {
        var error = SupabaseErrorMapping.MapException(new HttpRequestException("connection refused"), "Get");

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("Supabase.Get.Network");
        error.Message.Should().Contain("connection refused");
    }

    [Fact]
    public void ParseRetryAfter_WithDelta_ReturnsDelta()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42)) },
        };

        SupabaseErrorMapping.ParseRetryAfter(response).Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void ParseRetryAfter_WithHttpDate_ReturnsPositiveDelta()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(60)) },
        };

        SupabaseErrorMapping.ParseRetryAfter(response).Should().NotBeNull();
        SupabaseErrorMapping.ParseRetryAfter(response)!.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ParseRetryAfter_WhenAbsent_ReturnsNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        SupabaseErrorMapping.ParseRetryAfter(response).Should().BeNull();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };
}
