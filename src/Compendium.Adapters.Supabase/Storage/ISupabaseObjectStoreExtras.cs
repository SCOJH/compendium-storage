// -----------------------------------------------------------------------
// <copyright file="ISupabaseObjectStoreExtras.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// Supabase-specific storage operations that are <em>not</em> part of the portable
/// <c>Compendium.Abstractions.Storage.IObjectStore</c> port. Portable code depends on
/// <c>IObjectStore</c> only ; Supabase-aware code injects (or casts to) this interface.
/// The concrete <see cref="SupabaseObjectStore"/> implements both.
/// </summary>
public interface ISupabaseObjectStoreExtras
{
    /// <summary>
    /// Builds the public-bucket URL for a key (no expiry, no request). Only meaningful
    /// for public buckets — the adapter cannot verify bucket visibility without a
    /// management call, so the caller is responsible for using this on public buckets only.
    /// </summary>
    /// <param name="key">The tenant-relative object key.</param>
    /// <param name="transform">Optional on-the-fly image transformation.</param>
    /// <returns>The public URL, or a validation error for an invalid key.</returns>
    Result<Uri> GetPublicUrl(string key, ImageTransform? transform = null);

    /// <summary>
    /// Creates a single-use signed upload URL (Supabase <c>createSignedUploadUrl</c>).
    /// Distinct from an S3 presigned PUT : the returned token is one-shot.
    /// </summary>
    /// <param name="key">The tenant-relative object key.</param>
    /// <param name="expiresIn">The lifetime of the signed upload URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="SignedUpload"/> on success, or an error.</returns>
    Task<Result<SignedUpload>> CreateSignedUploadAsync(
        string key,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An on-the-fly image transformation applied to a public or rendered object URL.
/// </summary>
/// <param name="Width">Target width in pixels.</param>
/// <param name="Height">Target height in pixels.</param>
/// <param name="Resize">Resize mode (<c>cover</c> / <c>contain</c> / <c>fill</c>).</param>
/// <param name="Quality">Output quality (1–100) for lossy formats.</param>
public sealed record ImageTransform(
    int? Width = null,
    int? Height = null,
    string? Resize = null,
    int? Quality = null);

/// <summary>
/// The result of creating a single-use signed upload URL.
/// </summary>
/// <param name="UploadUrl">The absolute URL to <c>PUT</c> the object to.</param>
/// <param name="Token">The single-use upload token embedded in <paramref name="UploadUrl"/>.</param>
/// <param name="Key">The tenant-relative key the upload targets.</param>
public sealed record SignedUpload(Uri UploadUrl, string Token, string Key)
{
    /// <inheritdoc />
    public override string ToString() => $"SignedUpload(Key={Key}, UploadUrl=***, Token=***)";
}
