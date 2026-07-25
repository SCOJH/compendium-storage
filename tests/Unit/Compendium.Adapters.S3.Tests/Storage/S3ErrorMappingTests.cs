// -----------------------------------------------------------------------
// <copyright file="S3ErrorMappingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Amazon.S3;
using Compendium.Adapters.S3.Storage;
using Compendium.Core.Results;

namespace Compendium.Adapters.S3.Tests.Storage;

public class S3ErrorMappingTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound, "Storage.NotFound")]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden, "Storage.AccessDenied")]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict, "Storage.ConflictExists")]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.TooManyRequests, "Storage.Throttled")]
    public void Map_RoutesCanonicalStatusesToStorageErrors(
        HttpStatusCode status,
        ErrorType expectedType,
        string expectedCode)
    {
        // Arrange
        var ex = new AmazonS3Exception("boom") { StatusCode = status };

        // Act
        var actual = S3ErrorMapping.Map(ex, "Put", "k");

        // Assert
        actual.Type.Should().Be(expectedType);
        actual.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Failure)]
    public void Map_RoutesUnmappedStatusesToS3PrefixedCode(
        HttpStatusCode status,
        ErrorType expectedType)
    {
        // Arrange
        var ex = new AmazonS3Exception("boom")
        {
            StatusCode = status,
            ErrorCode = "Custom",
        };

        // Act
        var actual = S3ErrorMapping.Map(ex, "Put", "k");

        // Assert
        actual.Type.Should().Be(expectedType);
        actual.Code.Should().Be("S3.Put.Custom");
        actual.Message.Should().Contain("Put failed");
    }

    [Fact]
    public void Map_UnmappedStatus_UsesUnknown_WhenErrorCodeMissing()
    {
        // Arrange
        var ex = new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError };

        // Act
        var actual = S3ErrorMapping.Map(ex, "Get", "k");

        // Assert
        actual.Code.Should().Be("S3.Get.Unknown");
    }

    [Fact]
    public void Map_NotFound_EmbedsKeyInMessage()
    {
        // Arrange
        var ex = new AmazonS3Exception("nope") { StatusCode = HttpStatusCode.NotFound };

        // Act
        var actual = S3ErrorMapping.Map(ex, "Get", "missing-key");

        // Assert
        actual.Message.Should().Contain("missing-key");
    }

    [Fact]
    public void Map_NullException_Throws()
    {
        // Arrange / Act
        var act = () => S3ErrorMapping.Map(null!, "Get", "k");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
