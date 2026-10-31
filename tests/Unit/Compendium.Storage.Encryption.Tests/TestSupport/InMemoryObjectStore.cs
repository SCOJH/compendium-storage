// -----------------------------------------------------------------------
// <copyright file="InMemoryObjectStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Models;

namespace Compendium.Storage.Encryption.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IObjectStore"/> exposing the raw stored bytes, so tests can tamper with them.
/// Stored buffers are served without copies, so a 100 MiB object costs 100 MiB once.
/// </summary>
internal sealed class InMemoryObjectStore : IObjectStore
{
    private readonly Dictionary<string, MemoryStream> _objects = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether uploads are read with the synchronous Stream API.</summary>
    public bool UseSynchronousReads { get; set; }

    public byte[] this[string key]
    {
        get => _objects[key].ToArray();
        set => _objects[key] = new MemoryStream(value, 0, value.Length, writable: false, publiclyVisible: true);
    }

    public async Task<Result<ObjectInfo>> PutAsync(
        string key,
        Stream content,
        ObjectMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = new MemoryStream();
        if (UseSynchronousReads)
        {
            content.CopyTo(buffer);
        }
        else
        {
            await content.CopyToAsync(buffer, cancellationToken);
        }

        _objects[key] = buffer;
        return Describe(key, buffer.Length, metadata?.ContentType);
    }

    public Task<Result<ObjectStream>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_objects.TryGetValue(key, out var stored))
        {
            return Task.FromResult<Result<ObjectStream>>(StorageErrors.NotFound(key));
        }

        var content = new MemoryStream(stored.GetBuffer(), 0, (int)stored.Length, writable: false);
        return Task.FromResult<Result<ObjectStream>>(new ObjectStream(content, Describe(key, stored.Length, null)));
    }

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _objects.Remove(key);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(_objects.ContainsKey(key)));

    public Task<Result<ListPage>> ListAsync(ListOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<Uri>> GetPresignedUrlAsync(
        string key,
        PresignedAction action,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private static ObjectInfo Describe(string key, long size, string? contentType) =>
        new(key, size, "etag", contentType, DateTimeOffset.UnixEpoch);
}
