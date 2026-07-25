// -----------------------------------------------------------------------
// <copyright file="TenantKey.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Compendium.Adapters.Supabase.Security;

/// <summary>
/// Tenant-aware key composer for Supabase Storage object keys.
/// </summary>
/// <remarks>
/// <para>
/// Every operation on the adapter prepends <c>{tenantId}/</c> to the caller's key.
/// A tenant cannot reference another tenant's objects, even via a maliciously crafted
/// key, because path traversal segments (<c>..</c>) and absolute slashes are rejected.
/// </para>
/// <para>
/// Ported verbatim from the S3 adapter's <c>TenantKey</c> (which mirrors the validator
/// pattern from <c>Compendium.Adapters.PostgreSQL.Security.RowLevelSecurityExtensions.IsValidTenantId</c>).
/// </para>
/// </remarks>
public static partial class TenantKey
{
    /// <summary>
    /// Maximum tenant-id length, matching the PostgreSQL adapter.
    /// </summary>
    public const int MaxTenantIdLength = 255;

    private static readonly Regex _tenantIdValidationRegex = TenantIdRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex TenantIdRegex();

    /// <summary>
    /// Validates a tenant id : alpha-numeric, dashes, underscores ; max 255 chars.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <returns><c>true</c> if the identifier is safe to embed in a key.</returns>
    public static bool IsValidTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        if (tenantId.Length > MaxTenantIdLength)
        {
            return false;
        }

        return _tenantIdValidationRegex.IsMatch(tenantId);
    }

    /// <summary>
    /// Composes the storage key for a tenant-relative key.
    /// </summary>
    /// <param name="tenantId">Tenant identifier. Validated.</param>
    /// <param name="key">Tenant-relative key. Must be non-empty and free of path-traversal segments.</param>
    /// <returns>Storage key of the form <c>tenantId/key</c>.</returns>
    /// <exception cref="ArgumentException">Thrown for any invalid input.</exception>
    public static string Compose(string tenantId, string key)
    {
        if (!IsValidTenantId(tenantId))
        {
            throw new ArgumentException(
                $"Invalid tenant id: '{tenantId}'. Must be alpha-numeric, dashes or underscores ; max {MaxTenantIdLength} chars.",
                nameof(tenantId));
        }

        ValidateKey(key);

        return $"{tenantId}/{key}";
    }

    /// <summary>
    /// Returns the tenant-relative key from a storage key.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="storageKey">Storage key.</param>
    /// <returns>The key with the tenant prefix removed, or the original key when no prefix matched.</returns>
    public static string Strip(string tenantId, string storageKey)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        if (string.IsNullOrEmpty(tenantId))
        {
            return storageKey;
        }

        var prefix = $"{tenantId}/";
        return storageKey.StartsWith(prefix, StringComparison.Ordinal)
            ? storageKey[prefix.Length..]
            : storageKey;
    }

    /// <summary>
    /// Validates a tenant-relative key.
    /// </summary>
    /// <param name="key">Tenant-relative key.</param>
    /// <exception cref="ArgumentException">Thrown for empty / path-traversal / absolute keys.</exception>
    public static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must be non-empty.", nameof(key));
        }

        if (key.StartsWith('/'))
        {
            throw new ArgumentException("Key must not be absolute (no leading '/').", nameof(key));
        }

        // Block path traversal. Compare segments rather than substring so legitimate
        // names like ".env" or "report..pdf" stay valid.
        var segments = key.Split('/');
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw new ArgumentException(
                    "Key must not contain '..' path-traversal segments.",
                    nameof(key));
            }
        }
    }
}
