// -----------------------------------------------------------------------
// <copyright file="SupabaseCredentialTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;

namespace Compendium.Adapters.Supabase.Tests.Connections;

public class SupabaseCredentialTests
{
    private const string Secret = "super-secret-token-material-abc123";

    public static TheoryData<SupabaseCredential> AllCredentials() =>
    [
        new SupabaseCredential.ManagementToken(Secret),
        new SupabaseCredential.ServiceRoleKey(Secret),
        new SupabaseCredential.AnonKey(Secret),
    ];

    [Theory]
    [MemberData(nameof(AllCredentials))]
    public void ToString_DoesNotLeakSecretMaterial(SupabaseCredential credential)
    {
        var rendered = credential.ToString();

        rendered.Should().NotContain(Secret);
        rendered.Should().Contain("***");
    }

    [Fact]
    public void ToString_UsesTypeSpecificRedactedLabels()
    {
        new SupabaseCredential.ManagementToken(Secret).ToString().Should().Be("ManagementToken(***)");
        new SupabaseCredential.ServiceRoleKey(Secret).ToString().Should().Be("ServiceRoleKey(***)");
        new SupabaseCredential.AnonKey(Secret).ToString().Should().Be("AnonKey(***)");
    }

    [Fact]
    public void RecordEquality_HoldsOnMaterial()
    {
        new SupabaseCredential.ServiceRoleKey("k").Should().Be(new SupabaseCredential.ServiceRoleKey("k"));
        new SupabaseCredential.ServiceRoleKey("k").Should().NotBe(new SupabaseCredential.ServiceRoleKey("other"));
    }

    [Fact]
    public void DifferentCases_AreNotEqual_EvenWithSameMaterial()
    {
        SupabaseCredential serviceRole = new SupabaseCredential.ServiceRoleKey("k");
        SupabaseCredential anon = new SupabaseCredential.AnonKey("k");

        serviceRole.Should().NotBe(anon);
    }

    [Fact]
    public void Connection_CarriesUrlsAndCredential()
    {
        var connection = new SupabaseConnection
        {
            ProjectUrl = new Uri("https://ref.supabase.co"),
            StorageUrl = new Uri("http://localhost:5000/storage/v1"),
            Credential = new SupabaseCredential.ServiceRoleKey("k"),
        };

        connection.ProjectUrl.Should().Be(new Uri("https://ref.supabase.co"));
        connection.StorageUrl.Should().Be(new Uri("http://localhost:5000/storage/v1"));
        connection.ManagementUrl.Should().BeNull();
        connection.Credential.Should().BeOfType<SupabaseCredential.ServiceRoleKey>();
    }
}
