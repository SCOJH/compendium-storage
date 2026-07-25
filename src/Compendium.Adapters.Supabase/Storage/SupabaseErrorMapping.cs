// -----------------------------------------------------------------------
// <copyright file="SupabaseErrorMapping.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Compendium.Abstractions.Storage;

namespace Compendium.Adapters.Supabase.Storage;

/// <summary>
/// Maps a failed Supabase Storage REST response (or a transport exception) to a
/// Compendium <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// Canonical statuses route through the published <see cref="StorageErrors"/> factories
/// so consumers can match against the storage prefix (<c>Storage.NotFound</c>, ...):
/// 404 → <see cref="StorageErrors.NotFound"/> (or <see cref="StorageErrors.InvalidBucket"/>
/// when the body names a bucket), 403 → AccessDenied, 409 → ConflictExists,
/// 429 → Throttled (with <c>Retry-After</c>), 413 → ContentTooLarge.
/// </para>
/// <para>
/// Other statuses keep a Supabase-specific code (<c>Supabase.{operation}.{code}</c>) so
/// operators retain provider diagnostics in logs. The storage-api error body is parsed
/// as both the modern <c>{code, message}</c> and legacy <c>{statusCode, error, message}</c>
/// shapes ; a non-JSON body degrades gracefully to the raw text.
/// </para>
/// </remarks>
internal static class SupabaseErrorMapping
{
    /// <summary>
    /// Maps a failed HTTP response to a Compendium error, reading the response body.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="operation">Logical operation name (<c>Put</c>, <c>Get</c>, ...).</param>
    /// <param name="key">Tenant-relative key the operation targeted.</param>
    /// <param name="bucket">The bucket the operation targeted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapped <see cref="Error"/>.</returns>
    public static async Task<Error> MapAsync(
        HttpResponseMessage response,
        string operation,
        string key,
        string bucket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var (code, message) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var mentionsBucket = Mentions(code, "bucket") || Mentions(message, "bucket");

        // storage-api signals a missing object with an object-level not-found body that is
        // not always carried by an HTTP 404 (e.g. 400 { "code": "not_found" }). Normalize it
        // to Storage.NotFound so consumers (and ExistsAsync) can rely on the error type.
        if (!mentionsBucket && IndicatesMissing(code, message))
        {
            return StorageErrors.NotFound(key);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => mentionsBucket
                ? StorageErrors.InvalidBucket(bucket)
                : StorageErrors.NotFound(key),
            HttpStatusCode.Forbidden => StorageErrors.AccessDenied(key),
            HttpStatusCode.Conflict => StorageErrors.ConflictExists(key),
            HttpStatusCode.TooManyRequests => StorageErrors.Throttled(ParseRetryAfter(response)),
            HttpStatusCode.RequestEntityTooLarge => Error.Validation(
                $"{StorageErrors.Prefix}.ContentTooLarge",
                "Object payload exceeds the storage provider's maximum size."),
            HttpStatusCode.Unauthorized => Error.Unauthorized(
                BuildCode(operation, code), BuildMessage(operation, message)),
            HttpStatusCode.BadRequest when mentionsBucket => StorageErrors.InvalidBucket(bucket),
            HttpStatusCode.BadRequest => Error.Validation(
                BuildCode(operation, code), BuildMessage(operation, message)),
            _ when (int)response.StatusCode >= 500 => Error.Unavailable(
                BuildCode(operation, code), BuildMessage(operation, message)),
            _ => Error.Failure(BuildCode(operation, code), BuildMessage(operation, message)),
        };
    }

    /// <summary>
    /// Maps a transport-level exception (connection refused, DNS failure, timeout) to an
    /// unavailable error.
    /// </summary>
    /// <param name="ex">The transport exception.</param>
    /// <param name="operation">Logical operation name.</param>
    /// <returns>An unavailable <see cref="Error"/>.</returns>
    public static Error MapException(Exception ex, string operation)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return Error.Unavailable($"Supabase.{operation}.Network", $"{operation} failed: {ex.Message}");
    }

    /// <summary>
    /// Parses a <c>Retry-After</c> header (delta-seconds or HTTP-date) into a duration.
    /// Exposed for testing.
    /// </summary>
    /// <param name="response">The response carrying the header.</param>
    /// <returns>The retry duration, or <c>null</c> when absent/unparseable.</returns>
    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta;
        }

        if (retryAfter.Date.HasValue)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>
    /// Reads and parses the storage-api error body (both modern and legacy shapes).
    /// Exposed for testing.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed <c>(code, message)</c> pair ; either element may be <c>null</c>.</returns>
    internal static async Task<(string? Code, string? Message)> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, Truncate(body));
            }

            // Modern shape: { "code": "...", "message": "..." }
            // Legacy shape: { "statusCode": "...", "error": "...", "message": "..." }
            var code = GetString(root, "code") ?? GetString(root, "error");
            var message = GetString(root, "message") ?? GetString(root, "msg");
            return (code, message ?? Truncate(body));
        }
        catch (JsonException)
        {
            return (null, Truncate(body));
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool Mentions(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool IndicatesMissing(string? code, string? message) =>
        Mentions(code, "not_found")
        || Mentions(code, "notfound")
        || Mentions(code, "NoSuchKey")
        || Mentions(message, "not found")
        || Mentions(message, "does not exist");

    private static string BuildCode(string operation, string? code) =>
        $"Supabase.{operation}.{Sanitize(code) ?? "Unknown"}";

    private static string BuildMessage(string operation, string? message) =>
        $"{operation} failed: {message ?? "no error detail returned by Supabase."}";

    private static string? Sanitize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var chars = code.Where(c => !char.IsWhiteSpace(c)).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    private static string Truncate(string body) =>
        body.Length <= 500 ? body : body[..500];
}
