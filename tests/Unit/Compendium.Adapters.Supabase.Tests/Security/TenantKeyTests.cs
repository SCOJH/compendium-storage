// -----------------------------------------------------------------------
// <copyright file="TenantKeyTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Security;

namespace Compendium.Adapters.Supabase.Tests.Security;

public class TenantKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidTenantId_WhenNullOrWhitespace_ReturnsFalse(string? tenantId)
    {
        var actual = TenantKey.IsValidTenantId(tenantId);

        actual.Should().BeFalse();
    }

    [Theory]
    [InlineData("tenant-1")]
    [InlineData("tenant_42")]
    [InlineData("Tenant-ABC-123")]
    [InlineData("a")]
    [InlineData("0123456789")]
    public void IsValidTenantId_WhenWellFormed_ReturnsTrue(string tenantId)
    {
        var actual = TenantKey.IsValidTenantId(tenantId);

        actual.Should().BeTrue();
    }

    [Theory]
    [InlineData("tenant 1")] // space
    [InlineData("tenant;DROP TABLE")] // SQL-injection style
    [InlineData("tenant'--")] // single quote
    [InlineData("tenant/with/slash")]
    [InlineData("tenant.dot")] // dot is not allowed in tenant id
    [InlineData("tenant@domain")]
    [InlineData("../etc/passwd")] // path traversal injected as tenant
    public void IsValidTenantId_WhenContainsForbiddenCharacters_ReturnsFalse(string tenantId)
    {
        var actual = TenantKey.IsValidTenantId(tenantId);

        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValidTenantId_WhenLengthExceedsMax_ReturnsFalse()
    {
        var tenantId = new string('a', TenantKey.MaxTenantIdLength + 1);

        var actual = TenantKey.IsValidTenantId(tenantId);

        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValidTenantId_WhenLengthExactlyMax_ReturnsTrue()
    {
        var tenantId = new string('a', TenantKey.MaxTenantIdLength);

        var actual = TenantKey.IsValidTenantId(tenantId);

        actual.Should().BeTrue();
    }

    [Fact]
    public void Compose_HappyPath_PrependsTenantPrefix()
    {
        var actual = TenantKey.Compose("tenant-a", "invoices/2026/inv-001.pdf");

        actual.Should().Be("tenant-a/invoices/2026/inv-001.pdf");
    }

    [Theory]
    [InlineData("bad tenant")]
    [InlineData("")]
    [InlineData(null)]
    public void Compose_WhenTenantInvalid_Throws(string? tenantId)
    {
        var act = () => TenantKey.Compose(tenantId!, "k.txt");

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading-slash")]
    [InlineData("path/../escape")]
    [InlineData("../escape")]
    public void Compose_WhenKeyInvalid_Throws(string key)
    {
        var act = () => TenantKey.Compose("tenant-a", key);

        act.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("a/b/c.pdf")]
    [InlineData("report..pdf")] // double-dot inside a segment, not a traversal segment
    [InlineData(".env")]
    public void Compose_AcceptsLegitimateKeys(string key)
    {
        var actual = TenantKey.Compose("tenant-a", key);

        actual.Should().Be($"tenant-a/{key}");
    }

    [Fact]
    public void Strip_WhenPrefixPresent_RemovesPrefix()
    {
        var actual = TenantKey.Strip("tenant-a", "tenant-a/invoices/file.pdf");

        actual.Should().Be("invoices/file.pdf");
    }

    [Fact]
    public void Strip_WhenPrefixAbsent_ReturnsOriginal()
    {
        var actual = TenantKey.Strip("tenant-a", "tenant-b/file.pdf");

        actual.Should().Be("tenant-b/file.pdf");
    }

    [Fact]
    public void Strip_WhenTenantEmpty_ReturnsOriginal()
    {
        var actual = TenantKey.Strip(string.Empty, "x/y");

        actual.Should().Be("x/y");
    }
}
