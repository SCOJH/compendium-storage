// -----------------------------------------------------------------------
// <copyright file="SupabaseManagementErrorMapping.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Compendium.Adapters.Supabase.Storage;

namespace Compendium.Adapters.Supabase.Management.Http;

/// <summary>
/// Maps failed Supabase <em>Management-plane</em> responses (and transport exceptions) to
/// Compendium <see cref="Error"/> values. Storage-plane (bucket) failures reuse the runtime
/// <see cref="SupabaseErrorMapping"/> so bucket errors match the published
/// <c>Storage.*</c> factories.
/// </summary>
/// <remarks>
/// Callers that know the target ref/bucket handle <c>404</c> / <c>409</c> themselves
/// (<c>ProjectNotFound</c>, idempotent delete, <c>ConflictExists</c>); this mapper covers the
/// generic control-plane statuses: <c>401 → ManagementUnauthorized</c>,
/// <c>429 → Throttled</c> (with <c>Retry-After</c>), <c>5xx → Unavailable</c>, and a
/// diagnostic <c>Supabase.{operation}.{code}</c> for the rest. It never throws.
/// </remarks>
internal static class SupabaseManagementErrorMapping
{
    /// <summary>
    /// Maps a failed Management API response to an <see cref="Error"/>, reading the body.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="operation">Logical operation name (<c>CreateProject</c>, ...).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapped error.</returns>
    public static async Task<Error> MapManagementFailureAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return SupabaseErrors.ManagementUnauthorized();

            case HttpStatusCode.TooManyRequests:
                return SupabaseErrors.Throttled(SupabaseErrorMapping.ParseRetryAfter(response));
        }

        var (code, message) = await SupabaseErrorMapping
            .ReadBodyAsync(response, cancellationToken)
            .ConfigureAwait(false);

        if ((int)response.StatusCode >= 500)
        {
            return Error.Unavailable(BuildCode(operation, code, response), BuildMessage(operation, message));
        }

        return Error.Failure(BuildCode(operation, code, response), BuildMessage(operation, message));
    }

    /// <summary>
    /// Maps a failed Storage admin (bucket) response, reusing the runtime storage mapping so
    /// bucket errors carry the published <c>Storage.*</c> codes.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="operation">Logical operation name.</param>
    /// <param name="bucketId">The bucket id the operation targeted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapped error.</returns>
    public static Task<Error> MapStorageFailureAsync(
        HttpResponseMessage response,
        string operation,
        string bucketId,
        CancellationToken cancellationToken = default) =>
        SupabaseErrorMapping.MapAsync(response, operation, bucketId, bucketId, cancellationToken);

    /// <summary>
    /// Maps a transport-level exception (connection refused, DNS failure, timeout) to an
    /// unavailable error.
    /// </summary>
    /// <param name="ex">The transport exception.</param>
    /// <param name="operation">Logical operation name.</param>
    /// <returns>An unavailable error.</returns>
    public static Error MapException(Exception ex, string operation)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return Error.Unavailable($"Supabase.{operation}.Network", $"{operation} failed: {ex.Message}");
    }

    private static string BuildCode(string operation, string? code, HttpResponseMessage response)
    {
        var sanitized = Sanitize(code) ?? ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"Supabase.{operation}.{sanitized}";
    }

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
}
