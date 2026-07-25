// -----------------------------------------------------------------------
// <copyright file="BucketManagementCapabilityTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Connections;

namespace Compendium.Adapters.Supabase.Management.Tests.Capabilities;

/// <summary>
/// Pins the A2 capability change: <see cref="SupabaseCapability.BucketManagement"/> is
/// <c>Full</c> for a connection carrying a <c>service_role</c> key (either plane) and
/// <c>None</c> otherwise.
/// </summary>
public class BucketManagementCapabilityTests
{
    [Fact]
    public void ServiceRoleKey_Cloud_UnlocksBucketManagement()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.ServiceRoleKey("svc"),
        });

        caps.Supports(SupabaseCapability.BucketManagement).Should().BeTrue();
        caps.Entries[SupabaseCapability.BucketManagement].Level.Should().Be(SupabaseCapabilityLevel.Full);
    }

    [Fact]
    public void ServiceRoleKey_SelfHosted_UnlocksBucketManagement()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://supabase.internal.example.com"),
            ManagementUrl = new Uri("https://supabase.internal.example.com"),
            Credential = new SupabaseCredential.ServiceRoleKey("svc"),
        });

        caps.Plane.Should().Be("self-hosted");
        caps.Entries[SupabaseCapability.BucketManagement].Level.Should().Be(SupabaseCapabilityLevel.Full);
    }

    [Fact]
    public void AnonKey_DoesNotUnlockBucketManagement()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.AnonKey("anon"),
        });

        caps.Supports(SupabaseCapability.BucketManagement).Should().BeFalse();
        caps.Entries[SupabaseCapability.BucketManagement].Level.Should().Be(SupabaseCapabilityLevel.None);
        caps.Entries[SupabaseCapability.BucketManagement].Limitation.Should().Contain("service_role");
    }

    [Fact]
    public void ManagementToken_DoesNotUnlockBucketManagement()
    {
        var caps = SupabaseCapabilities.For(new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            Credential = new SupabaseCredential.ManagementToken("pat"),
        });

        caps.Supports(SupabaseCapability.BucketManagement).Should().BeFalse();
    }

    [Fact]
    public void ProjectPlane_DoesNotUnlockBucketManagement()
    {
        var caps = SupabaseCapabilities.ForProjectPlane();

        caps.Supports(SupabaseCapability.BucketManagement).Should().BeFalse();
    }
}
