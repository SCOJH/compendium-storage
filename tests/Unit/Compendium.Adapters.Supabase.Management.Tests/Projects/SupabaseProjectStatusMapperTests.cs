// -----------------------------------------------------------------------
// <copyright file="SupabaseProjectStatusMapperTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Management.Projects;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class SupabaseProjectStatusMapperTests
{
    [Theory]
    [InlineData("ACTIVE_HEALTHY", SupabaseProjectStatus.Active)]
    [InlineData("COMING_UP", SupabaseProjectStatus.Provisioning)]
    [InlineData("INITIALIZING", SupabaseProjectStatus.Provisioning)]
    [InlineData("RESTORING", SupabaseProjectStatus.Provisioning)]
    [InlineData("RESTARTING", SupabaseProjectStatus.Provisioning)]
    [InlineData("UPGRADING", SupabaseProjectStatus.Provisioning)]
    [InlineData("UNPAUSING", SupabaseProjectStatus.Provisioning)]
    [InlineData("ACTIVE_UNHEALTHY", SupabaseProjectStatus.Provisioning)]
    [InlineData("GOING_DOWN", SupabaseProjectStatus.Deprovisioning)]
    [InlineData("PAUSING", SupabaseProjectStatus.Deprovisioning)]
    [InlineData("REMOVING", SupabaseProjectStatus.Deprovisioning)]
    [InlineData("REMOVED", SupabaseProjectStatus.Deleted)]
    [InlineData("INIT_FAILED", SupabaseProjectStatus.Failed)]
    [InlineData("RESTORE_FAILED", SupabaseProjectStatus.Failed)]
    [InlineData("INACTIVE", SupabaseProjectStatus.Failed)]
    [InlineData("PAUSED", SupabaseProjectStatus.Failed)]
    public void Map_KnownStates_NormalizeToLifecycle(string raw, SupabaseProjectStatus expected)
    {
        SupabaseProjectStatusMapper.Map(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("active_healthy")]
    [InlineData("  ACTIVE_HEALTHY  ")]
    public void Map_IsCaseAndWhitespaceInsensitive(string raw)
    {
        SupabaseProjectStatusMapper.Map(raw).Should().Be(SupabaseProjectStatus.Active);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SOMETHING_BRAND_NEW")]
    public void Map_UnknownOrBlank_ReturnsUnknown(string? raw)
    {
        SupabaseProjectStatusMapper.Map(raw).Should().Be(SupabaseProjectStatus.Unknown);
    }
}
