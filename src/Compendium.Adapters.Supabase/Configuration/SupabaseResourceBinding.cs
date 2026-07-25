// -----------------------------------------------------------------------
// <copyright file="SupabaseResourceBinding.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Microsoft.Extensions.Configuration;

namespace Compendium.Adapters.Supabase.Configuration;

/// <summary>
/// A Supabase connection resolved from the platform's injected-resource configuration
/// convention. When a platform (e.g. Nexus) provisions a Supabase project for an app it
/// injects the resolved endpoint and keys as environment variables under a per-resource
/// prefix ; this helper binds them back into a usable <see cref="SupabaseConnection"/> plus
/// the raw key material.
/// </summary>
/// <remarks>
/// <para>
/// The convention is, for a resource named <c>{name}</c> :
/// </para>
/// <list type="table">
///   <item><term><c>Resources__{name}__Url</c></term><description>project URL (required)</description></item>
///   <item><term><c>Resources__{name}__AnonKey</c></term><description>anon key (optional)</description></item>
///   <item><term><c>Resources__{name}__ServiceRoleKey</c></term><description>service_role key (optional)</description></item>
///   <item><term><c>Resources__{name}__DbConnectionString</c></term><description>Postgres connection string (optional)</description></item>
///   <item><term><c>Resources__{name}__Bucket</c></term><description>default bucket (optional)</description></item>
/// </list>
/// <para>
/// The double-underscore form is how .NET's environment-variable configuration provider maps
/// onto the hierarchical key <c>Resources:{name}:Url</c>. Exactly one of the keys is required
/// to build a credential ; <see cref="ServiceRoleKey"/> is preferred over <see cref="AnonKey"/>.
/// All token material redacts in <c>ToString()</c>.
/// </para>
/// </remarks>
public sealed record SupabaseResourceBinding
{
    /// <summary>The configuration root under which resources are injected.</summary>
    public const string ResourcesSection = "Resources";

    /// <summary>Gets the connection built from the resource's URL and best credential.</summary>
    public required SupabaseConnection Connection { get; init; }

    /// <summary>Gets the project URL as configured.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the anon key, when configured.</summary>
    public string? AnonKey { get; init; }

    /// <summary>Gets the service_role key, when configured.</summary>
    public string? ServiceRoleKey { get; init; }

    /// <summary>Gets the Postgres connection string, when configured.</summary>
    public string? DbConnectionString { get; init; }

    /// <summary>Gets the default bucket, when configured.</summary>
    public string? Bucket { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"SupabaseResourceBinding(Url={Url}, Bucket={Bucket ?? "(none)"}, AnonKey={Redact(AnonKey)}, " +
        $"ServiceRoleKey={Redact(ServiceRoleKey)}, DbConnectionString={Redact(DbConnectionString)})";

    /// <summary>
    /// Binds a <see cref="SupabaseResourceBinding"/> from configuration under
    /// <c>Resources:{resourceName}</c>. Returns a failure (never throws) when the resource
    /// is absent or lacks a URL and a usable key.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="resourceName">The injected resource name.</param>
    /// <returns>The bound resource, or a <c>Supabase.NotConfigured</c> failure.</returns>
    public static Result<SupabaseResourceBinding> FromConfiguration(IConfiguration configuration, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return SupabaseErrors.NotConfigured("a resource name is required");
        }

        var section = configuration.GetSection($"{ResourcesSection}:{resourceName}");

        var url = section["Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return SupabaseErrors.NotConfigured($"{ResourcesSection}:{resourceName}:Url is required");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var projectUri))
        {
            return SupabaseErrors.NotConfigured($"{ResourcesSection}:{resourceName}:Url '{url}' is not a valid absolute URL");
        }

        var serviceRoleKey = Trimmed(section["ServiceRoleKey"]);
        var anonKey = Trimmed(section["AnonKey"]);

        SupabaseCredential? credential = serviceRoleKey is not null
            ? new SupabaseCredential.ServiceRoleKey(serviceRoleKey)
            : anonKey is not null
                ? new SupabaseCredential.AnonKey(anonKey)
                : null;

        if (credential is null)
        {
            return SupabaseErrors.NotConfigured(
                $"{ResourcesSection}:{resourceName} requires a ServiceRoleKey or AnonKey");
        }

        var connection = new SupabaseConnection
        {
            ProjectUrl = projectUri,
            Credential = credential,
        };

        return new SupabaseResourceBinding
        {
            Connection = connection,
            Url = url,
            AnonKey = anonKey,
            ServiceRoleKey = serviceRoleKey,
            DbConnectionString = Trimmed(section["DbConnectionString"]),
            Bucket = Trimmed(section["Bucket"]),
        };
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Redact(string? value) =>
        string.IsNullOrEmpty(value) ? "(none)" : "***";
}
