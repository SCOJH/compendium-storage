// -----------------------------------------------------------------------
// <copyright file="RedirectingHttpClientFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Management.Tests.TestSupport;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that rewrites every outgoing request's authority
/// (scheme/host/port) onto a target server, preserving path + query. Lets the adapter build
/// real <c>https://api.supabase.com/...</c> URLs while the request lands on a local WireMock
/// server.
/// </summary>
internal sealed class RedirectingHttpClientFactory : IHttpClientFactory
{
    private readonly Uri _target;

    public RedirectingHttpClientFactory(Uri target) => _target = target;

    public HttpClient CreateClient(string name) => new(new RedirectingHandler(_target));

    private sealed class RedirectingHandler : DelegatingHandler
    {
        private readonly Uri _target;

        public RedirectingHandler(Uri target)
            : base(new HttpClientHandler()) => _target = target;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                request.RequestUri = new UriBuilder(request.RequestUri)
                {
                    Scheme = _target.Scheme,
                    Host = _target.Host,
                    Port = _target.Port,
                }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
