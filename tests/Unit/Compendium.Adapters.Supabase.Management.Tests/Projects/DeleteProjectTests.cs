// -----------------------------------------------------------------------
// <copyright file="DeleteProjectTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class DeleteProjectTests
{
    private const string Ref = "abcdefghijklmnop";

    [Fact]
    public async Task DeleteProject_OnSuccess_ReturnsSuccess()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("DELETE", $"/v1/projects/{Ref}", 200);

        var result = await harness.Admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProject_On404_IsIdempotentSuccess()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("DELETE", $"/v1/projects/{Ref}", 404);

        var result = await harness.Admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProject_On401_ReturnsManagementUnauthorized()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("DELETE", $"/v1/projects/{Ref}", 401);

        var result = await harness.Admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.ManagementUnauthorized");
    }

    [Fact]
    public async Task DeleteProject_On5xx_ReturnsUnavailable()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("DELETE", $"/v1/projects/{Ref}", 500);

        var result = await harness.Admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task DeleteProject_EmptyRef_ReturnsValidationFailure()
    {
        using var harness = new SupabaseAdminHarness();

        var result = await harness.Admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), "");

        result.Error.Code.Should().Be("Supabase.DeleteProject.InvalidRef");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteProject_SelfHosted_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var selfHosted = new SupabaseConnection
        {
            ManagementUrl = new Uri("https://supabase.internal.example.com"),
            Credential = new SupabaseCredential.ManagementToken("pat"),
        };

        var result = await harness.Admin.DeleteProjectAsync(selfHosted, Ref);

        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
    }

    [Fact]
    public async Task DeleteProject_OnTransportFailure_ReturnsUnavailable()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);

        var result = await admin.DeleteProjectAsync(SupabaseAdminHarness.CloudManagement(), Ref);

        result.Error.Code.Should().Be("Supabase.DeleteProject.Network");
    }
}
