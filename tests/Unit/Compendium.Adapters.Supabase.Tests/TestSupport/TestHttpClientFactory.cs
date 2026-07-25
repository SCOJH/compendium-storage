// -----------------------------------------------------------------------
// <copyright file="TestHttpClientFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Tests.TestSupport;

/// <summary>
/// Test <see cref="IHttpClientFactory"/> that hands out fresh <see cref="HttpClient"/>
/// instances over a shared handler. The handler is <em>not</em> disposed when a client is
/// disposed (<c>disposeHandler: false</c>), so the store's per-operation
/// <c>using var http = factory.CreateClient(...)</c> can run many times against the same
/// mocked handler.
/// </summary>
internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public TestHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
