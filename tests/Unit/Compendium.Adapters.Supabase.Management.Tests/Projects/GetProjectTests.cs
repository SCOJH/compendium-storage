// -----------------------------------------------------------------------
// <copyright file="GetProjectTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Projects;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class GetProjectTests
{
    private const string Ref = "abcdefghijklmnop";

    [Fact]
    public async Task GetProject_WhenActiveHealthy_ReturnsActive()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", $"/v1/projects/{Ref}", 200,
            $$"""{"id":"{{Ref}}","name":"n","organization_id":"org","region":"eu-central-1","status":"ACTIVE_HEALTHY"}""");

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SupabaseProjectStatus.Active);
        result.Value.OrganizationSlug.Should().Be("org");
        result.Value.Region.Should().Be("eu-central-1");
        result.Value.ProjectUrl.Should().Be(new Uri($"https://{Ref}.supabase.co"));
    }

    [Fact]
    public async Task GetProject_WhenComingUp_ReturnsProvisioning()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", $"/v1/projects/{Ref}", 200,
            $$"""{"id":"{{Ref}}","name":"n","status":"COMING_UP"}""");

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Value.Status.Should().Be(SupabaseProjectStatus.Provisioning);
    }

    [Fact]
    public async Task GetProject_WhenUnknownStatusString_ReturnsUnknown()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("GET", $"/v1/projects/{Ref}", 200,
            $$"""{"id":"{{Ref}}","name":"n","status":"SOMETHING_NEW"}""");

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Value.Status.Should().Be(SupabaseProjectStatus.Unknown);
        result.Value.RawStatus.Should().Be("SOMETHING_NEW");
    }

    [Fact]
    public async Task GetProject_On404_ReturnsProjectNotFound()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", $"/v1/projects/{Ref}", 404);

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Supabase.ProjectNotFound");
        result.Error.Metadata.Should().ContainKey("projectRef");
    }

    [Fact]
    public async Task GetProject_On401_ReturnsManagementUnauthorized()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("GET", $"/v1/projects/{Ref}", 401);

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Code.Should().Be("Supabase.ManagementUnauthorized");
    }

    [Fact]
    public async Task GetProject_EmptyRef_ReturnsValidationFailure()
    {
        using var harness = new SupabaseAdminHarness();

        var result = await harness.Admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), "  ");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Supabase.GetProject.InvalidRef");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProject_KeysOnlyConnection_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var keysOnly = new SupabaseConnection
        {
            ManagementUrl = new Uri("https://api.supabase.com"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await harness.Admin.GetProjectAsync(keysOnly, Ref);

        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
    }

    [Fact]
    public async Task GetProject_OnTransportFailure_ReturnsUnavailable()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);

        var result = await admin.GetProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Supabase.GetProject.Network");
    }

    [Fact]
    public async Task GetProject_NullConnection_Throws()
    {
        using var harness = new SupabaseAdminHarness();

        var act = () => harness.Admin.GetProjectAsync(null!, Ref);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
