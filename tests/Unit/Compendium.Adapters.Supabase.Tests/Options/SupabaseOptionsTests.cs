// -----------------------------------------------------------------------
// <copyright file="SupabaseOptionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Compendium.Adapters.Supabase.Options;

namespace Compendium.Adapters.Supabase.Tests.Options;

public class SupabaseOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new SupabaseOptions();

        options.DefaultPresignedUrlExpiry.Should().Be(TimeSpan.FromMinutes(15));
        options.Url.Should().BeEmpty();
        options.Bucket.Should().BeEmpty();
        options.StorageUrl.Should().BeNull();
        options.ServiceRoleKey.Should().BeNull();
        options.AnonKey.Should().BeNull();
    }

    [Fact]
    public void SectionName_IsCanonical()
    {
        SupabaseOptions.SectionName.Should().Be("Compendium:Adapters:Supabase");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-a-url", false)]
    [InlineData("https://ref.supabase.co", true)]
    public void Url_IsRequiredAndMustBeAUrl(string url, bool expectedValid)
    {
        var options = new SupabaseOptions { Url = url, Bucket = "b" };

        DataAnnotationsValid(options).Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("bucket-1", true)]
    public void Bucket_IsRequired(string bucket, bool expectedValid)
    {
        var options = new SupabaseOptions { Url = "https://ref.supabase.co", Bucket = bucket };

        DataAnnotationsValid(options).Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("not-a-url", false)]
    [InlineData("http://localhost:5000/storage/v1", true)]
    public void StorageUrl_WhenPresent_MustBeAUrl(string storageUrl, bool expectedValid)
    {
        var options = new SupabaseOptions
        {
            Url = "https://ref.supabase.co",
            Bucket = "b",
            StorageUrl = storageUrl,
        };

        DataAnnotationsValid(options).Should().Be(expectedValid);
    }

    [Fact]
    public void OneKeyValidator_WhenNeitherKeySet_Fails()
    {
        var result = new SupabaseOptionsValidator().Validate(null, Options("b", serviceRole: null, anon: null));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("neither");
    }

    [Fact]
    public void OneKeyValidator_WhenBothKeysSet_Fails()
    {
        var result = new SupabaseOptionsValidator().Validate(null, Options("b", serviceRole: "s", anon: "a"));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("both");
    }

    [Fact]
    public void OneKeyValidator_WhenOnlyServiceRoleSet_Succeeds()
    {
        var result = new SupabaseOptionsValidator().Validate(null, Options("b", serviceRole: "s", anon: null));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OneKeyValidator_WhenOnlyAnonSet_Succeeds()
    {
        var result = new SupabaseOptionsValidator().Validate(null, Options("b", serviceRole: null, anon: "a"));

        result.Succeeded.Should().BeTrue();
    }

    private static SupabaseOptions Options(string bucket, string? serviceRole, string? anon) => new()
    {
        Url = "https://ref.supabase.co",
        Bucket = bucket,
        ServiceRoleKey = serviceRole,
        AnonKey = anon,
    };

    private static bool DataAnnotationsValid(SupabaseOptions options)
    {
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);
    }
}
