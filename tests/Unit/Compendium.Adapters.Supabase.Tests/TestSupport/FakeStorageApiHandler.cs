// -----------------------------------------------------------------------
// <copyright file="FakeStorageApiHandler.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Compendium.Adapters.Supabase.Tests.TestSupport;

/// <summary>
/// A stateful, in-memory simulation of the Supabase Storage REST API, sufficient to run
/// the <c>IObjectStore</c> behavioural contract against the real <c>SupabaseObjectStore</c>
/// without Docker. Objects are keyed by their bucket-qualified path (e.g.
/// <c>assets/tenant-a/docs/a.txt</c>).
/// </summary>
internal sealed class FakeStorageApiHandler : HttpMessageHandler
{
    private const string Root = "/storage/v1";

    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var absolutePath = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var rel = absolutePath.StartsWith(Root, StringComparison.Ordinal) ? absolutePath[Root.Length..] : absolutePath;

        if (request.Method == HttpMethod.Post && rel.StartsWith("/object/list/", StringComparison.Ordinal))
        {
            return await ListAsync(request, rel, cancellationToken).ConfigureAwait(false);
        }

        if (request.Method == HttpMethod.Post && rel.StartsWith("/object/upload/sign/", StringComparison.Ordinal))
        {
            var key = rel["/object/upload/sign/".Length..];
            return Json(HttpStatusCode.OK, $$"""{"url":"/object/upload/sign/{{key}}?token=fake","token":"fake"}""");
        }

        if (request.Method == HttpMethod.Post && rel.StartsWith("/object/sign/", StringComparison.Ordinal))
        {
            var key = rel["/object/sign/".Length..];
            return _objects.ContainsKey(key)
                ? Json(HttpStatusCode.OK, $$"""{"signedURL":"/object/sign/{{key}}?token=fake"}""")
                : Json(HttpStatusCode.NotFound, """{"code":"NoSuchKey","message":"Object not found"}""");
        }

        if (request.Method == HttpMethod.Get && rel.StartsWith("/object/info/", StringComparison.Ordinal))
        {
            return Info(rel["/object/info/".Length..]);
        }

        if (request.Method == HttpMethod.Get && rel.StartsWith("/object/", StringComparison.Ordinal))
        {
            return Get(rel["/object/".Length..]);
        }

        if (request.Method == HttpMethod.Delete && rel.StartsWith("/object/", StringComparison.Ordinal))
        {
            var key = rel["/object/".Length..];
            return _objects.Remove(key)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Json(HttpStatusCode.NotFound, """{"code":"NoSuchKey","message":"Object not found"}""");
        }

        if (request.Method == HttpMethod.Post && rel.StartsWith("/object/", StringComparison.Ordinal))
        {
            return await PutAsync(request, rel["/object/".Length..], cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> PutAsync(HttpRequestMessage request, string key, CancellationToken cancellationToken)
    {
        var data = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = request.Content?.Headers.ContentType?.MediaType ?? "application/octet-stream";
        _objects[key] = new StoredObject(data, contentType);
        return Json(HttpStatusCode.OK, $$"""{"Key":"{{key}}"}""");
    }

    private HttpResponseMessage Get(string key)
    {
        if (!_objects.TryGetValue(key, out var stored))
        {
            return Json(HttpStatusCode.NotFound, """{"code":"NoSuchKey","message":"Object not found"}""");
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stored.Data) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(stored.ContentType);
        response.Content.Headers.LastModified = stored.LastModified;
        response.Headers.ETag = new EntityTagHeaderValue($"\"{stored.ETag}\"");
        return response;
    }

    private HttpResponseMessage Info(string key)
    {
        if (!_objects.TryGetValue(key, out var stored))
        {
            return Json(HttpStatusCode.NotFound, """{"code":"NoSuchKey","message":"Object not found"}""");
        }

        var name = key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(Record(name, stored)));
    }

    private async Task<HttpResponseMessage> ListAsync(HttpRequestMessage request, string rel, CancellationToken cancellationToken)
    {
        var bucket = rel["/object/list/".Length..];
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var prefix = string.Empty;
        if (!string.IsNullOrEmpty(body))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("prefix", out var p) && p.ValueKind == JsonValueKind.String)
            {
                prefix = p.GetString() ?? string.Empty;
            }
        }

        var fullPrefix = $"{bucket}/{prefix}";
        var records = new List<object>();
        foreach (var (key, stored) in _objects)
        {
            if (!key.StartsWith(fullPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var leaf = key[fullPrefix.Length..];
            if (leaf.Length == 0)
            {
                continue;
            }

            records.Add(Record(leaf, stored));
        }

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(records));
    }

    private static object Record(string name, StoredObject stored) => new
    {
        name,
        id = "row",
        updated_at = stored.LastModified,
        metadata = new
        {
            size = stored.Data.Length,
            mimetype = stored.ContentType,
            eTag = $"\"{stored.ETag}\"",
            lastModified = stored.LastModified,
        },
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record StoredObject(byte[] Data, string ContentType)
    {
        public string ETag { get; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
    }
}
