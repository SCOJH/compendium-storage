// -----------------------------------------------------------------------
// <copyright file="Stub.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Compendium.Adapters.Supabase.Management.Tests.TestSupport;

/// <summary>Small WireMock stubbing helpers to keep the tests terse.</summary>
internal static class Stub
{
    public static void Json(this WireMockServer server, string method, string path, int status, string body) =>
        server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create()
                .WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    public static void Status(this WireMockServer server, string method, string path, int status) =>
        server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status));

    public static void Throttled(this WireMockServer server, string method, string path, int retryAfterSeconds) =>
        server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
