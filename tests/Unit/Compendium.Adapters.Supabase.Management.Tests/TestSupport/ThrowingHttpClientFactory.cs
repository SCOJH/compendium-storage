// -----------------------------------------------------------------------
// <copyright file="ThrowingHttpClientFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Tests.TestSupport;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients always fail the send with an
/// <see cref="HttpRequestException"/> — simulates a transport failure (connection refused,
/// DNS error) so the adapter's <c>Error.Unavailable</c> mapping can be exercised without a
/// live socket.
/// </summary>
internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new ThrowingHandler());

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated transport failure");
    }
}
