// -----------------------------------------------------------------------
// <copyright file="CreateProjectTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Projects;
using Compendium.Adapters.Supabase.Management.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Supabase.Management.Tests.Projects;

public class CreateProjectTests
{
    private static SupabaseProjectSpec Spec(string? plan = "pro") => new()
    {
        Name = "my-proj",
        OrganizationSlug = "org-1",
        Region = "eu-central-1",
        DbPassword = "s3cret",
        Plan = plan,
    };

    [Fact]
    public async Task CreateProject_OnSuccess_ReturnsProvisioningProject()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 201,
            """{"id":"abcdefghijklmnop","organization_id":"org-1","name":"my-proj","region":"eu-central-1","status":"COMING_UP","created_at":"2026-07-24T10:00:00Z"}""");

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsSuccess.Should().BeTrue();
        result.Value.Ref.Should().Be("abcdefghijklmnop");
        result.Value.Name.Should().Be("my-proj");
        result.Value.Status.Should().Be(SupabaseProjectStatus.Provisioning);
        result.Value.Plane.Should().Be("cloud");
        result.Value.RawStatus.Should().Be("COMING_UP");
        result.Value.ProjectUrl.Should().Be(new Uri("https://abcdefghijklmnop.supabase.co"));
        result.Value.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-07-24T10:00:00Z"));
    }

    [Fact]
    public async Task CreateProject_WhenStatusOmitted_DefaultsToProvisioning()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 201,
            """{"id":"abcdefghijklmnop","name":"my-proj"}""");

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SupabaseProjectStatus.Provisioning);
        result.Value.RawStatus.Should().BeNull();
    }

    [Fact]
    public async Task CreateProject_SendsBearerTokenAndSnakeCaseBody()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 201, """{"id":"ref1","name":"my-proj"}""");

        await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement("my-pat"), Spec("pro"));

        var request = harness.Server.LogEntries.Should().ContainSingle().Which.RequestMessage;
        request.Headers!["Authorization"].Should().Contain("Bearer my-pat");
        request.Body.Should().Contain("\"organization_id\":\"org-1\"");
        request.Body.Should().Contain("\"db_pass\":\"s3cret\"");
        request.Body.Should().Contain("\"region\":\"eu-central-1\"");
        request.Body.Should().Contain("\"plan\":\"pro\"");
    }

    [Fact]
    public async Task CreateProject_WhenPlanNull_OmitsPlanFromBody()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 201, """{"id":"ref1","name":"my-proj"}""");

        await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec(plan: null));

        var request = harness.Server.LogEntries.Should().ContainSingle().Which.RequestMessage;
        request.Body.Should().NotContain("plan");
    }

    [Fact]
    public async Task CreateProject_On401_ReturnsManagementUnauthorized()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Status("POST", "/v1/projects", 401);

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Supabase.ManagementUnauthorized");
    }

    [Fact]
    public async Task CreateProject_On429_ReturnsThrottledWithRetryAfter()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Throttled("POST", "/v1/projects", retryAfterSeconds: 30);

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.TooManyRequests);
        result.Error.Code.Should().Be("Supabase.Throttled");
        result.Error.Metadata.Should().ContainKey("retryAfterSeconds");
        result.Error.Metadata["retryAfterSeconds"].Should().Be(30d);
    }

    [Fact]
    public async Task CreateProject_On5xx_ReturnsUnavailable()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 503, """{"message":"upstream down"}""");

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().StartWith("Supabase.CreateProject");
    }

    [Fact]
    public async Task CreateProject_On400WithCodeBody_SurfacesSanitizedCode()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 400,
            """{"code":"invalid_request","message":"organization not found"}""");

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Failure);
        result.Error.Code.Should().Be("Supabase.CreateProject.invalid_request");
        result.Error.Message.Should().Contain("organization not found");
    }

    [Fact]
    public async Task CreateProject_WhenResponseHasNoRef_ReturnsEmptyResponse()
    {
        using var harness = new SupabaseAdminHarness();
        harness.Server.Json("POST", "/v1/projects", 201, "{}");

        var result = await harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.CreateProject.EmptyResponse");
    }

    [Fact]
    public async Task CreateProject_KeysOnlyConnection_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var keysOnly = new SupabaseConnection
        {
            ManagementUrl = new Uri("https://api.supabase.com"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        var result = await harness.Admin.CreateProjectAsync(keysOnly, Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
        harness.Server.LogEntries.Should().BeEmpty("the gate must fail before any HTTP call");
    }

    [Fact]
    public async Task CreateProject_SelfHostedConnection_FailsCapabilityGate()
    {
        using var harness = new SupabaseAdminHarness();
        var selfHosted = new SupabaseConnection
        {
            ManagementUrl = new Uri("https://supabase.internal.example.com"),
            Credential = new SupabaseCredential.ManagementToken("pat"),
        };

        var result = await harness.Admin.CreateProjectAsync(selfHosted, Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supabase.CapabilityNotSupported");
    }

    [Fact]
    public async Task CreateProject_OnTransportFailure_ReturnsUnavailable()
    {
        var admin = new SupabaseAdmin(new ThrowingHttpClientFactory(), NullLogger<SupabaseAdmin>.Instance);

        var result = await admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), Spec());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be("Supabase.CreateProject.Network");
    }

    [Fact]
    public async Task CreateProject_NullArguments_Throw()
    {
        using var harness = new SupabaseAdminHarness();

        var actConnection = () => harness.Admin.CreateProjectAsync(null!, Spec());
        var actSpec = () => harness.Admin.CreateProjectAsync(SupabaseAdminHarness.CloudManagement(), null!);

        await actConnection.Should().ThrowAsync<ArgumentNullException>();
        await actSpec.Should().ThrowAsync<ArgumentNullException>();
    }
}
