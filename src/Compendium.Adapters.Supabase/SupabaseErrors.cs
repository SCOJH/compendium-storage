// -----------------------------------------------------------------------
// <copyright file="SupabaseErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Capabilities;

namespace Compendium.Adapters.Supabase;

/// <summary>
/// Adapter-local (non-storage) error definitions. Storage operations reuse the
/// published <c>Compendium.Abstractions.Storage.StorageErrors</c> factories via
/// <see cref="Storage.SupabaseErrorMapping"/>; these cover Supabase-specific failures
/// (capability gating, misconfiguration) that have no storage-port equivalent.
/// </summary>
public static class SupabaseErrors
{
    /// <summary>
    /// Gets the error code prefix for adapter-local Supabase errors.
    /// </summary>
    public const string Prefix = "Supabase";

    /// <summary>
    /// The operation requires a capability the connection does not support (or supports
    /// only partially). Carries <c>plane</c> and <c>capability</c> metadata; see the
    /// adapter's CAPABILITIES.md for the support matrix.
    /// </summary>
    /// <param name="plane">The connection plane (<c>"cloud"</c> / <c>"self-hosted"</c>).</param>
    /// <param name="capability">The unsupported capability.</param>
    /// <param name="limitation">Optional human-readable limitation note.</param>
    /// <returns>A validation error with code <c>Supabase.CapabilityNotSupported</c>.</returns>
    public static Error NotSupported(string plane, SupabaseCapability capability, string? limitation = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["plane"] = plane,
            ["capability"] = capability.ToString(),
        };
        if (limitation is not null)
        {
            metadata["limitation"] = limitation;
        }

        return Error.Validation(
            $"{Prefix}.CapabilityNotSupported",
            limitation is null
                ? $"The '{plane}' Supabase connection does not support capability '{capability}'."
                : $"The '{plane}' Supabase connection does not support capability '{capability}': {limitation}",
            metadata);
    }

    /// <summary>
    /// The adapter is missing configuration required to build a usable connection
    /// (absent project/storage URL, a management-only credential passed to a
    /// project-plane call, ...).
    /// </summary>
    /// <param name="what">A short description of what is missing or wrong.</param>
    /// <returns>An unavailable error with code <c>Supabase.NotConfigured</c>.</returns>
    public static Error NotConfigured(string what) =>
        Error.Unavailable($"{Prefix}.NotConfigured", $"Supabase adapter is not configured: {what}.");

    /// <summary>
    /// A management-plane call was rejected by the Supabase Cloud Management API with a
    /// <c>401</c> — the personal access token is missing, expired, or lacks scope.
    /// </summary>
    /// <returns>An unauthorized error with code <c>Supabase.ManagementUnauthorized</c>.</returns>
    public static Error ManagementUnauthorized() =>
        Error.Unauthorized(
            $"{Prefix}.ManagementUnauthorized",
            "The Supabase Management API rejected the personal access token (401). Check the token and its scope.");

    /// <summary>
    /// A project referenced by ref does not exist on the Management API.
    /// </summary>
    /// <param name="projectRef">The project ref that was not found.</param>
    /// <returns>A not-found error with code <c>Supabase.ProjectNotFound</c>.</returns>
    public static Error ProjectNotFound(string projectRef) =>
        Error.NotFound(
            $"{Prefix}.ProjectNotFound",
            $"Supabase project '{projectRef}' was not found.",
            new Dictionary<string, object> { ["projectRef"] = projectRef });

    /// <summary>
    /// A project exists but is not in a usable state for the requested operation (still
    /// coming up, restoring, or its keys are not yet provisioned). Retryable — the caller
    /// or saga should poll with backoff.
    /// </summary>
    /// <param name="projectRef">The project ref.</param>
    /// <param name="state">The current upstream state (or a short reason).</param>
    /// <returns>An unavailable error with code <c>Supabase.ProjectNotReady</c>.</returns>
    public static Error ProjectNotReady(string projectRef, string state) =>
        Error.Unavailable(
            $"{Prefix}.ProjectNotReady",
            $"Supabase project '{projectRef}' is not ready ({state}).",
            new Dictionary<string, object>
            {
                ["projectRef"] = projectRef,
                ["state"] = state,
            });

    /// <summary>
    /// A bring-your-own / self-hosted project failed its attach-time health-and-auth probe.
    /// </summary>
    /// <param name="projectUrl">The probed project (or storage) URL.</param>
    /// <param name="reason">A short description of why the probe failed.</param>
    /// <returns>An unavailable error with code <c>Supabase.AttachFailed</c>.</returns>
    public static Error AttachFailed(Uri projectUrl, string reason)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);
        return Error.Unavailable(
            $"{Prefix}.AttachFailed",
            $"Could not attach the Supabase project at '{projectUrl}': {reason}.",
            new Dictionary<string, object>
            {
                ["projectUrl"] = projectUrl.ToString(),
                ["reason"] = reason,
            });
    }

    /// <summary>
    /// The Management API throttled the request (<c>429</c>). Carries the server's
    /// <c>Retry-After</c> hint when present ; the caller (or saga) owns the backoff.
    /// </summary>
    /// <param name="retryAfter">The server-advised retry delay, when present.</param>
    /// <returns>A too-many-requests error with code <c>Supabase.Throttled</c>.</returns>
    public static Error Throttled(TimeSpan? retryAfter = null)
    {
        var metadata = new Dictionary<string, object>();
        if (retryAfter.HasValue)
        {
            metadata["retryAfterSeconds"] = retryAfter.Value.TotalSeconds;
        }

        return Error.TooManyRequests(
            $"{Prefix}.Throttled",
            retryAfter.HasValue
                ? $"The Supabase Management API throttled the request. Retry after {retryAfter.Value.TotalSeconds} seconds."
                : "The Supabase Management API throttled the request. Please retry later.",
            metadata);
    }
}
