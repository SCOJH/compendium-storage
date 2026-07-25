// -----------------------------------------------------------------------
// <copyright file="S3ErrorMapping.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Amazon.S3;
using Compendium.Abstractions.Storage;
using Compendium.Core.Results;

namespace Compendium.Adapters.S3.Storage;

/// <summary>
/// Maps <see cref="AmazonS3Exception"/> to a Compendium <see cref="Error"/>.
/// </summary>
/// <remarks>
/// HTTP-status → error :
/// <list type="bullet">
///   <item>404 → <see cref="StorageErrors.NotFound"/></item>
///   <item>403 → <see cref="StorageErrors.AccessDenied"/></item>
///   <item>429 → <see cref="StorageErrors.Throttled"/></item>
///   <item>409 → <see cref="StorageErrors.ConflictExists"/></item>
///   <item>others (401 / 503 / 5xx / unmapped) → <see cref="Error.Failure"/> with
///     code <c>S3.{operation}.{S3ErrorCode}</c> and a type that mirrors the HTTP status.</item>
/// </list>
/// <para>
/// The canonical 404 / 403 / 409 / 429 paths route through <see cref="StorageErrors"/>
/// so consumers can match against the published prefix (<c>Storage.NotFound</c>, ...).
/// All other failures keep the S3-specific code (<c>S3.{Put|Get|...}.{AccessDenied|...}</c>)
/// so logs retain provider diagnostics.
/// </para>
/// </remarks>
internal static class S3ErrorMapping
{
    /// <summary>
    /// Maps an <see cref="AmazonS3Exception"/> to a Compendium error.
    /// </summary>
    /// <param name="ex">Source exception.</param>
    /// <param name="operation">Logical operation name (e.g. <c>Put</c>, <c>Get</c>).</param>
    /// <param name="key">Storage key (already tenant-prefixed) the operation targeted.</param>
    /// <returns>Mapped <see cref="Error"/>.</returns>
    public static Error Map(AmazonS3Exception ex, string operation, string key)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex.StatusCode switch
        {
            HttpStatusCode.NotFound => StorageErrors.NotFound(key),
            HttpStatusCode.Forbidden => StorageErrors.AccessDenied(key),
            HttpStatusCode.Conflict => StorageErrors.ConflictExists(key),
            HttpStatusCode.TooManyRequests => StorageErrors.Throttled(),
            HttpStatusCode.Unauthorized => Error.Unauthorized(BuildCode(ex, operation), BuildMessage(ex, operation)),
            HttpStatusCode.ServiceUnavailable => Error.Unavailable(BuildCode(ex, operation), BuildMessage(ex, operation)),
            _ => Error.Failure(BuildCode(ex, operation), BuildMessage(ex, operation)),
        };
    }

    private static string BuildCode(AmazonS3Exception ex, string operation)
        => $"S3.{operation}.{ex.ErrorCode ?? "Unknown"}";

    private static string BuildMessage(AmazonS3Exception ex, string operation)
        => $"{operation} failed: {ex.Message}";
}
