// -----------------------------------------------------------------------
// <copyright file="StaticTenantContext.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Multitenancy;

namespace Compendium.Adapters.Supabase.Tests.TestSupport;

/// <summary>Test-only <see cref="ITenantContextAccessor"/> that returns a fixed tenant (or none).</summary>
internal sealed class StaticTenantContextAccessor : ITenantContextAccessor
{
    public StaticTenantContextAccessor(string? tenantId)
    {
        TenantContext = new StaticTenantContext(tenantId);
    }

    public ITenantContext TenantContext { get; }
}

internal sealed class StaticTenantContext : ITenantContext
{
    public StaticTenantContext(string? tenantId)
    {
        TenantId = tenantId;
        TenantName = tenantId;
    }

    public string? TenantId { get; }

    public string? TenantName { get; }

    public TenantInfo? CurrentTenant => null;

    public bool HasTenant => !string.IsNullOrEmpty(TenantId);
}
