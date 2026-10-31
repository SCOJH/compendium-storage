// -----------------------------------------------------------------------
// <copyright file="StorageEncryptionErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Storage.Encryption;

/// <summary>
/// Provides the errors returned by <see cref="EncryptingObjectStore"/>. No message carries key material.
/// </summary>
public static class StorageEncryptionErrors
{
    /// <summary>
    /// Gets the error code prefix for storage-encryption errors.
    /// </summary>
    public const string Prefix = "Storage.Encryption";

    /// <summary>
    /// No tenant is in scope, so no tenant key can be used.
    /// </summary>
    /// <returns>The error.</returns>
    public static Error NoTenant() =>
        Error.Forbidden($"{Prefix}.NoTenant", "No tenant in scope ; refusing operation.");

    /// <summary>
    /// The key-encryption key identifier is empty or longer than 255 UTF-8 bytes.
    /// </summary>
    /// <returns>The error.</returns>
    public static Error InvalidKekId() =>
        Error.Validation($"{Prefix}.InvalidKekId", "The key-encryption key identifier must be 1 to 255 UTF-8 bytes.");

    /// <summary>
    /// The key provider returned a data key of the wrong size.
    /// </summary>
    /// <returns>The error.</returns>
    public static Error InvalidDataKey() =>
        Error.Unexpected($"{Prefix}.InvalidDataKey", "The key provider returned a data key of the wrong size.");

    /// <summary>
    /// The object does not start with a valid encrypted-object header.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <returns>The error.</returns>
    public static Error InvalidHeader(string key) =>
        Error.Failure($"{Prefix}.InvalidHeader", $"Object '{key}' is not an encrypted object or its header is damaged.");

    /// <summary>
    /// The object was written in a format version this library does not read.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="version">The version found in the header.</param>
    /// <returns>The error.</returns>
    public static Error UnsupportedVersion(string key, int version) =>
        Error.Failure($"{Prefix}.UnsupportedVersion", $"Object '{key}' uses unsupported encryption format version {version}.");

    /// <summary>
    /// The object failed verification: wrong data key, or content modified, truncated or reordered.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <returns>The error.</returns>
    public static Error IntegrityCheckFailed(string key) =>
        Error.Failure(
            $"{Prefix}.IntegrityCheckFailed",
            $"Object '{key}' failed integrity verification: wrong key, or content modified, truncated or reordered.");

    /// <summary>
    /// The underlying store reported success without reading the whole encrypted content.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <returns>The error.</returns>
    public static Error IncompleteWrite(string key) =>
        Error.Failure(
            $"{Prefix}.IncompleteWrite",
            $"The store did not consume the whole encrypted content of object '{key}' ; the upload was discarded.");
}
