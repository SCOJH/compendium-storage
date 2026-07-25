// -----------------------------------------------------------------------
// <copyright file="SupabaseCapabilitiesTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Connections;

namespace Compendium.Adapters.Supabase.Tests.Capabilities;

public class SupabaseCapabilitiesTests
{
    [Fact]
    public void For_ServiceRoleKey_CloudDefault_HasStorageButNoManagement()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        });

        caps.Plane.Should().Be("cloud");
        caps.Supports(SupabaseCapability.ObjectStorage).Should().BeTrue();
        caps.Supports(SupabaseCapability.PresignedGet).Should().BeTrue();
        caps.Supports(SupabaseCapability.SignedUpload).Should().BeTrue(); // Partial still counts as supported
        caps.Supports(SupabaseCapability.PublicUrl).Should().BeTrue();
        caps.Supports(SupabaseCapability.ProjectProvisioning).Should().BeFalse();
        caps.Supports(SupabaseCapability.ProjectKeys).Should().BeFalse();
    }

    [Fact]
    public void For_SignedUpload_IsPartialWithLimitation()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.AnonKey("k"),
        });

        caps.Entries[SupabaseCapability.SignedUpload].Level.Should().Be(SupabaseCapabilityLevel.Partial);
        caps.Entries[SupabaseCapability.SignedUpload].Limitation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void For_ManagementToken_Cloud_UnlocksProvisioning()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.ManagementToken("pat"),
        });

        caps.Plane.Should().Be("cloud");
        caps.Supports(SupabaseCapability.ProjectProvisioning).Should().BeTrue();
        caps.Supports(SupabaseCapability.ProjectKeys).Should().BeTrue();
        caps.Supports(SupabaseCapability.ProjectPause).Should().BeTrue();
    }

    [Fact]
    public void For_ManagementToken_SelfHosted_DoesNotUnlockProvisioning()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://supabase.internal.example.com"),
            ManagementUrl = new Uri("https://supabase.internal.example.com"),
            Credential = new SupabaseCredential.ManagementToken("pat"),
        });

        caps.Plane.Should().Be("self-hosted");
        caps.Supports(SupabaseCapability.ProjectProvisioning).Should().BeFalse();
        caps.Entries[SupabaseCapability.ProjectProvisioning].Limitation.Should().Contain("AttachProjectAsync");
    }

    [Fact]
    public void ForProjectPlane_HasStorageButNoManagement()
    {
        var caps = SupabaseCapabilities.ForProjectPlane();

        caps.Supports(SupabaseCapability.ObjectStorage).Should().BeTrue();
        caps.Supports(SupabaseCapability.ProjectProvisioning).Should().BeFalse();
        caps.Supports(SupabaseCapability.RealtimeBroadcast).Should().BeFalse();
    }

    [Fact]
    public void EnsureSupported_WhenSupported_ReturnsSuccess()
    {
        var caps = SupabaseCapabilities.ForProjectPlane();

        caps.EnsureSupported(SupabaseCapability.PublicUrl).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureSupported_WhenUnsupported_ReturnsCapabilityNotSupportedFailure()
    {
        var caps = SupabaseCapabilities.ForProjectPlane();

        var result = caps.EnsureSupported(SupabaseCapability.ProjectProvisioning);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
        result.Error.Metadata.Should().ContainKey("capability");
        result.Error.Metadata.Should().ContainKey("limitation");
    }

    [Fact]
    public void For_NullConnection_Throws()
    {
        var act = () => SupabaseCapabilities.For(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
