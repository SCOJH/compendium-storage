// -----------------------------------------------------------------------
// <copyright file="SupabaseAdminHarness.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.Server;

namespace Compendium.Adapters.Supabase.Management.Tests.TestSupport;

/// <summary>
/// Per-test harness: a real WireMock server plus a <see cref="SupabaseAdmin"/> whose HTTP
/// client is redirected onto that server. Management-plane requests target
/// <c>https://api.supabase.com</c> and are rewritten onto WireMock by
/// <see cref="RedirectingHttpClientFactory"/>; storage-plane connections point their
/// <c>StorageUrl</c>/<c>ProjectUrl</c> straight at WireMock.
/// </summary>
internal sealed class SupabaseAdminHarness : IDisposable
{
    public SupabaseAdminHarness()
    {
        Server = WireMockServer.Start();
        BaseUrl = new Uri(Server.Urls[0]);
        Admin = new SupabaseAdmin(new RedirectingHttpClientFactory(BaseUrl), NullLogger<SupabaseAdmin>.Instance);
    }

    /// <summary>Gets the running WireMock server (configure stubs on it).</summary>
    public WireMockServer Server { get; }

    /// <summary>Gets the WireMock base URL.</summary>
    public Uri BaseUrl { get; }

    /// <summary>Gets the facade under test.</summary>
    public ISupabaseAdmin Admin { get; }

    /// <summary>A cloud management connection (Bearer PAT) — routed to WireMock via redirect.</summary>
    public static SupabaseConnection CloudManagement(string token = "pat") => new()
    {
        ManagementUrl = new Uri("https://api.supabase.com"),
        Credential = new SupabaseCredential.ManagementToken(token),
    };

    /// <summary>A project-plane connection with a service_role key, storage pointed at WireMock.</summary>
    public SupabaseConnection ServiceRoleStorage(string key = "service-role") => new()
    {
        StorageUrl = BaseUrl,
        Credential = new SupabaseCredential.ServiceRoleKey(key),
    };

    /// <summary>A project-plane connection whose ProjectUrl is WireMock (storage base = /storage/v1).</summary>
    public SupabaseConnection ServiceRoleProject(string key = "service-role") => new()
    {
        ProjectUrl = BaseUrl,
        Credential = new SupabaseCredential.ServiceRoleKey(key),
    };

    public void Dispose() => Server.Stop();
}
