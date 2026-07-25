// -----------------------------------------------------------------------
// <copyright file="SupabaseObjectStoreContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Adapters.Supabase.Capabilities;
using Compendium.Adapters.Supabase.Storage;
using Compendium.Adapters.Supabase.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Tests.Contract;

/// <summary>
/// Runs the <see cref="ObjectStoreContractTests"/> against the real
/// <see cref="SupabaseObjectStore"/> backed by an in-memory storage-api simulation
/// (<see cref="FakeStorageApiHandler"/>) — no Docker required.
/// </summary>
public sealed class SupabaseObjectStoreContractTests : ObjectStoreContractTests
{
    protected override IObjectStore CreateStore()
    {
        var context = new SupabaseStorageContext
        {
            StorageBaseUrl = "https://ref.supabase.co/storage/v1",
            ApiKey = "service-role-key",
            Bucket = "assets",
            DefaultPresignedUrlExpiry = TimeSpan.FromMinutes(15),
        };

        return new SupabaseObjectStore(
            new TestHttpClientFactory(new FakeStorageApiHandler()),
            context,
            SupabaseCapabilities.ForProjectPlane(),
            new StaticTenantContextAccessor("contract-tenant"),
            NullLogger<SupabaseObjectStore>.Instance);
    }
}
