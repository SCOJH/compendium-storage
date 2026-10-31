// -----------------------------------------------------------------------
// <copyright file="EncryptionHarness.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Abstractions.Storage.Models;
using Compendium.Multitenancy;

namespace Compendium.Storage.Encryption.Tests.TestSupport;

/// <summary>
/// An <see cref="EncryptingObjectStore"/> over an <see cref="InMemoryObjectStore"/> and an
/// <see cref="InMemoryKeyProvider"/>, with a switchable tenant in scope.
/// </summary>
internal sealed class EncryptionHarness
{
    public const string TenantA = "tenant-a";

    public const int Block = 64 * 1024;

    public const int SealedBlock = Block + 16;

    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    public EncryptionHarness(IObjectStore? inner = null, IKeyEncryptionKeyProvider? keys = null)
    {
        Inner = inner ?? Memory;
        Keys = keys ?? KeyProvider;
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.TenantContext.Returns(_tenantContext);
        SetTenant(TenantA);
        Store = new EncryptingObjectStore(Inner, Keys, accessor);
    }

    public InMemoryObjectStore Memory { get; } = new();

    public InMemoryKeyProvider KeyProvider { get; } = new();

    public IObjectStore Inner { get; }

    public IKeyEncryptionKeyProvider Keys { get; }

    public EncryptingObjectStore Store { get; }

    /// <summary>Header length for an object written under <paramref name="kekId"/> (ASCII).</summary>
    public static int HeaderLength(string kekId) => 6 + kekId.Length + 7;

    public void SetTenant(string? tenantId)
    {
        _tenantContext.HasTenant.Returns(tenantId is not null);
        _tenantContext.TenantId.Returns(tenantId);
    }

    public async Task<EncryptedObject> PutAsync(string key, byte[] plaintext)
    {
        var put = await Store.PutAsync(key, new MemoryStream(plaintext));
        put.IsSuccess.Should().BeTrue(put.IsFailure ? put.Error.Code : null);
        return put.Value!;
    }

    public async Task<EncryptedObject> PutPatternAsync(string key, long length)
    {
        var put = await Store.PutAsync(key, new PatternStream(length));
        put.IsSuccess.Should().BeTrue(put.IsFailure ? put.Error.Code : null);
        return put.Value!;
    }

    public Task<ReadOutcome> ReadAsync(EncryptedObject obj, int chunk = 81920, bool synchronous = false) =>
        ReadAsync(obj.StorageKey, obj.WrappedDek, obj.KekId, chunk, synchronous);

    public async Task<ReadOutcome> ReadAsync(
        string key,
        ReadOnlyMemory<byte> wrappedDek,
        string kekId,
        int chunk = 81920,
        bool synchronous = false)
    {
        var open = await Store.OpenReadAsync(key, wrappedDek, kekId);
        if (open.IsFailure)
        {
            return new ReadOutcome(open, [], null);
        }

        await using var stream = open.Value!;
        var content = new MemoryStream();
        var buffer = new byte[chunk];
        try
        {
            while (true)
            {
                var read = synchronous
                    ? stream.Content.Read(buffer, 0, buffer.Length)
                    : await stream.Content.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                content.Write(buffer, 0, read);
            }
        }
        catch (Exception ex)
        {
            return new ReadOutcome(open, content.ToArray(), ex);
        }

        return new ReadOutcome(open, content.ToArray(), null);
    }
}

/// <summary>
/// The outcome of opening and draining an encrypted object.
/// </summary>
/// <param name="Open">The result of <see cref="IEncryptedObjectStore.OpenReadAsync"/>.</param>
/// <param name="Content">The plaintext bytes returned before the end or the failure.</param>
/// <param name="ReadError">The exception thrown while reading, if any.</param>
internal sealed record ReadOutcome(Result<ObjectStream> Open, byte[] Content, Exception? ReadError)
{
    public bool Succeeded => Open.IsSuccess && ReadError is null;
}
