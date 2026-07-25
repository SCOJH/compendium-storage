// -----------------------------------------------------------------------
// <copyright file="GetProjectKeysTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class GetProjectKeysTests
{
    private const string Ref = "abcdefghijklmnop";
    private const string KeysPath = "/v1/projects/abcdefghijklmnop/api-keys";

    [Fact]
    public async Task GetProjectKeys_OnSuccess_ReturnsKeysAndProjectUrl()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", KeysPath, 200,
            """[{"name":"anon","api_key":"anon-key"},{"name":"service_role","api_key":"service-key"}]""");

        var result = await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsSuccess.Should().BeTrue();
        result.Value.AnonKey.Should().Be("anon-key");
        result.Value.ServiceRoleKey.Should().Be("service-key");
        result.Value.ProjectUrl.Should().Be(new Uri($"https://{Ref}.supabase.co"));
    }

    [Fact]
    public async Task GetProjectKeys_RequestsRevealTrue()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", KeysPath, 200,
            """[{"name":"anon","api_key":"a"},{"name":"service_role","api_key":"s"}]""");

        await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        var request = harness.Server.LogEntries.Should().ContainSingle().Which.RequestMessage;
        request.Url.Should().Contain("reveal=true");
    }

    [Fact]
    public async Task GetProjectKeys_WhenServiceKeyMissing_ReturnsProjectNotReady()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", KeysPath, 200, """[{"name":"anon","api_key":"anon-key"}]""");

        var result = await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Supabase.ProjectNotReady");
    }

    [Fact]
    public async Task GetProjectKeys_On404_ReturnsProjectNotFound()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", KeysPath, 404);

        var result = await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Code.Should().Be("Supabase.ProjectNotFound");
    }

    [Fact]
    public async Task GetProjectKeys_On429_ReturnsThrottled()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Throttled("GET", KeysPath, retryAfterSeconds: 5);

        var result = await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Code.Should().Be("Supabase.Throttled");
        result.Error.Metadata["retryAfterSeconds"].Should().Be(5d);
    }

    [Fact]
    public async Task GetProjectKeys_KeysOnlyConnection_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var keysOnly = new SupabaseConnection
        {
            ManagementUrl = new Uri("https://api.supabase.com"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await harness.Admin.GetProjectKeysAsync(keysOnly, Ref);

        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
    }

    [Fact]
    public async Task GetProjectKeys_EmptyRef_ReturnsValidationFailure()
    {
        using var harness = new SupabaseAdminHarness();

        var result = await harness.Admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), "");

        result.Error.Code.Should().Be("Supabase.GetProjectKeys.InvalidRef");
    }

    [Fact]
    public async Task GetProjectKeys_OnTransportFailure_ReturnsUnavailable()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);

        var result = await admin.GetProjectKeysAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Code.Should().Be("Supabase.GetProjectKeys.Network");
    }
}
