// -----------------------------------------------------------------------
// <copyright file="S3OptionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Compendium.Adapters.S3.Options;

namespace Compendium.Adapters.S3.Tests.Options;

public class S3OptionsTests
{
    [Fact]
    public void S3Options_Defaults_AreSensible()
    {
        // Arrange / Act
        var options = new S3Options();

        // Assert
        options.MultipartThresholdBytes.Should().Be(S3Options.DefaultMultipartThresholdBytes);
        options.MultipartThresholdBytes.Should().Be(8L * 1024L * 1024L);
        options.DefaultPresignedUrlExpiry.Should().Be(TimeSpan.FromMinutes(15));
        options.ServerSideEncryption.Should().Be(ServerSideEncryption.None);
        options.ForcePathStyle.Should().BeFalse();
        options.Bucket.Should().BeEmpty();
        options.AccessKey.Should().BeNull();
        options.SecretKey.Should().BeNull();
        options.Region.Should().BeNull();
        options.ServiceUrl.Should().BeNull();
        options.KmsKeyId.Should().BeNull();
    }

    [Fact]
    public void S3Options_SectionName_IsCanonical()
    {
        // Assert
        S3Options.SectionName.Should().Be("Compendium:Adapters:S3");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("bucket-1", true)]
    public void S3Options_BucketIsRequired(string bucket, bool expectedValid)
    {
        // Arrange
        var options = new S3Options { Bucket = bucket };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("not-a-url", false)]
    [InlineData("https://minio.internal:9000", true)]
    public void S3Options_ServiceUrl_Validates(string url, bool expectedValid)
    {
        // Arrange
        var options = new S3Options { Bucket = "b", ServiceUrl = url };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().Be(expectedValid);
    }

    [Fact]
    public void S3Options_MultipartThreshold_RejectsZero()
    {
        // Arrange
        var options = new S3Options { Bucket = "b", MultipartThresholdBytes = 0 };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().BeFalse();
    }
}
