// -----------------------------------------------------------------------
// <copyright file="StubTenantContext.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Multitenancy;

namespace Compendium.Adapters.Supabase.Management.Tests.TestSupport;

/// <summary>Test-only <see cref="ITenantContextAccessor"/> — enough for the runtime store to resolve.</summary>
internal sealed class StubTenantContextAccessor : ITenantContextAccessor
{
    public ITenantContext TenantContext { get; } = new StubTenantContext();

    private sealed class StubTenantContext : ITenantContext
    {
        public string? TenantId => "tenant";

        public string? TenantName => "tenant";

        public TenantInfo? CurrentTenant => null;

        public bool HasTenant => true;
    }
}
