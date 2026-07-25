// -----------------------------------------------------------------------
// <copyright file="AttachProjectTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Projects;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class AttachProjectTests
{
    [Fact]
    public async Task Attach_WhenStorageProbeSucceeds_ReturnsAttachedSelfHostedActive()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", "/bucket", 200, "[]");

        var result = await harness.Admin.AttachProjectAsync(harness.ServiceRoleStorage());

        result.IsSuccess.Should().BeTrue();
        result.Value.Plane.Should().Be("self-hosted");
        result.Value.Status.Should().Be(SupabaseProjectStatus.Active);
        result.Value.Ref.Should().Be(harness.BaseUrl.Host);
        harness.Server.LogEntries.Should().ContainSingle("a healthy storage probe needs no fallback");
    }

    [Fact]
    public async Task Attach_WhenCredentialRejected_FailsWithoutFallback()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", "/bucket", 401);
        harness.Server.Status("GET", "/auth/v1/health", 200); // present, but must not be consulted

        var result = await harness.Admin.AttachProjectAsync(harness.ServiceRoleStorage());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.AttachFailed");
        result.Error.Message.Should().Contain("rejected");
    }

    [Fact]
    public async Task Attach_WhenStorageUnroutedButHealthOk_AttachesAsUnknown()
    {
        // Health 200 proves reachability, not credential validity (GoTrue health does not
        // enforce the apikey on every proxy config) — the attach must NOT claim Active.
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", "/storage/v1/bucket", 404);
        harness.Server.Json("GET", "/auth/v1/health", 200, """{"name":"GoTrue"}""");

        var result = await harness.Admin.AttachProjectAsync(harness.ServiceRoleProject());

        result.IsSuccess.Should().BeTrue();
        result.Value.Plane.Should().Be("self-hosted");
        result.Value.Status.Should().Be(SupabaseProjectStatus.Unknown);
        result.Value.RawStatus.Should().Be("attached-unverified");
    }

    [Fact]
    public async Task Attach_WhenStorageAndHealthBothFail_FailsAttach()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", "/storage/v1/bucket", 404);
        harness.Server.Status("GET", "/auth/v1/health", 500);

        var result = await harness.Admin.AttachProjectAsync(harness.ServiceRoleProject());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.AttachFailed");
        result.Error.Message.Should().Contain("500");
    }

    [Fact]
    public async Task Attach_ManagementTokenOnly_ReturnsNotConfigured()
    {
        using var harness = new SupabaseAdminHarness();
        var connection = new SupabaseConnection
        {
            ProjectUrl = harness.BaseUrl,
            Credential = new SupabaseCredential.ManagementToken("pat"),
        };

        var result = await harness.Admin.AttachProjectAsync(connection);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.NotConfigured");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_NoUrl_ReturnsNotConfigured()
    {
        using var harness = new SupabaseAdminHarness();
        var connection = new SupabaseConnection
        {
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await harness.Admin.AttachProjectAsync(connection);

        result.Error.Code.Should().Be("Supabase.NotConfigured");
    }

    [Fact]
    public async Task Attach_TransportFailureWithProjectUrl_FailsAttach()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);
        var connection = new SupabaseConnection
        {
            ProjectUrl = new Uri("https://tenant.example.com"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await admin.AttachProjectAsync(connection);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.AttachFailed");
    }

    [Fact]
    public async Task Attach_TransportFailureWithoutProjectUrl_FailsAttach()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);
        var connection = new SupabaseConnection
        {
            StorageUrl = new Uri("https://tenant.example.com/storage/v1"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await admin.AttachProjectAsync(connection);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.AttachFailed");
    }

    [Fact]
    public async Task Attach_NullConnection_Throws()
    {
        using var harness = new SupabaseAdminHarness();

        var act = () => harness.Admin.AttachProjectAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
