// -----------------------------------------------------------------------
// <copyright file="SupabaseCapabilities.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;

namespace Compendium.Adapters.Supabase.Capabilities;

/// <summary>
/// The declarative capability matrix of a Supabase connection. Unlike the Git adapter
/// (where the provider fixes the matrix), Supabase capability derivation is
/// <em>per connection</em> : a management token against Supabase Cloud unlocks the
/// project-provisioning capabilities, while a keys-only or self-hosted connection does
/// not. Consumers use it to drive UI affordances ; adapters use
/// <see cref="EnsureSupported"/> to fail uniformly.
/// </summary>
public sealed record SupabaseCapabilities
{
    private static readonly string SelfHostedManagementLimitation =
        "self-hosted Supabase exposes no Management API — use AttachProjectAsync";

    private static readonly string KeysOnlyManagementLimitation =
        "keys-only connection: supply a ManagementToken for management-plane operations";

    private static readonly string OptionsManagementLimitation =
        "options-configured project-plane adapter has no Management API access";

    private static readonly string SignedUploadLimitation =
        "single-use signed-upload token; not S3 presigned-PUT semantics";

    private static readonly string BucketManagementLimitation =
        "bucket management (create/list/delete) requires a service_role key; use ISupabaseAdmin from Compendium.Adapters.Supabase.Management";

    private static readonly string RealtimeLimitation =
        "deferred to a future phase";

    /// <summary>
    /// Gets the connection plane these capabilities describe (<c>"cloud"</c> / <c>"self-hosted"</c>).
    /// </summary>
    public required string Plane { get; init; }

    /// <summary>
    /// Gets the declared support per capability. Capabilities absent from the dictionary
    /// are treated as <see cref="SupabaseCapabilityLevel.None"/>.
    /// </summary>
    public required IReadOnlyDictionary<SupabaseCapability, SupabaseCapabilitySupport> Entries { get; init; }

    /// <summary>
    /// Returns whether the capability is available at any level
    /// (<see cref="SupabaseCapabilityLevel.Partial"/> or <see cref="SupabaseCapabilityLevel.Full"/>).
    /// </summary>
    /// <param name="capability">The capability to test.</param>
    /// <returns><c>true</c> when supported at Partial or Full level.</returns>
    public bool Supports(SupabaseCapability capability) =>
        Entries.TryGetValue(capability, out var support) && support.Level != SupabaseCapabilityLevel.None;

    /// <summary>
    /// Returns success when the capability is available, otherwise the standard
    /// <c>Supabase.CapabilityNotSupported</c> failure with the capability's limitation note.
    /// </summary>
    /// <param name="capability">The capability required by the caller.</param>
    /// <returns>A success or failure <see cref="Result"/>.</returns>
    public Result EnsureSupported(SupabaseCapability capability) =>
        Supports(capability)
            ? Result.Success()
            : Result.Failure(SupabaseErrors.NotSupported(
                Plane,
                capability,
                Entries.TryGetValue(capability, out var support) ? support.Limitation : null));

    /// <summary>
    /// Derives the capability matrix for a specific connection.
    /// </summary>
    /// <param name="connection">The connection to inspect.</param>
    /// <returns>The derived capability matrix.</returns>
    public static SupabaseCapabilities For(SupabaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var isCloudManagement = connection.ManagementUrl is null
            || connection.ManagementUrl.Host.EndsWith("supabase.com", StringComparison.OrdinalIgnoreCase);
        var plane = isCloudManagement ? "cloud" : "self-hosted";
        var managementAvailable = connection.Credential is SupabaseCredential.ManagementToken && isCloudManagement;
        var managementLimitation = isCloudManagement ? KeysOnlyManagementLimitation : SelfHostedManagementLimitation;

        // Bucket management (create/list/delete over the Storage REST admin API) works on
        // BOTH planes, but is a full-privilege operation: it requires a service_role key.
        // An anon key or a management-only token cannot administer buckets.
        var bucketManagement = connection.Credential is SupabaseCredential.ServiceRoleKey
            ? new SupabaseCapabilitySupport(SupabaseCapabilityLevel.Full)
            : new SupabaseCapabilitySupport(SupabaseCapabilityLevel.None, BucketManagementLimitation);

        return new SupabaseCapabilities
        {
            Plane = plane,
            Entries = BuildEntries(managementAvailable, managementLimitation, bucketManagement),
        };
    }

    /// <summary>
    /// The capability matrix for an options-configured project-plane adapter (no
    /// connection object). Storage capabilities are available ; management capabilities
    /// are <see cref="SupabaseCapabilityLevel.None"/>.
    /// </summary>
    /// <returns>The project-plane capability matrix.</returns>
    public static SupabaseCapabilities ForProjectPlane() => new()
    {
        Plane = "cloud",
        Entries = BuildEntries(
            managementAvailable: false,
            OptionsManagementLimitation,
            // The options-configured runtime store exposes no bucket-admin surface; bucket
            // management is reached through ISupabaseAdmin built from an explicit connection.
            new SupabaseCapabilitySupport(SupabaseCapabilityLevel.None, BucketManagementLimitation)),
    };

    private static IReadOnlyDictionary<SupabaseCapability, SupabaseCapabilitySupport> BuildEntries(
        bool managementAvailable,
        string managementLimitation,
        SupabaseCapabilitySupport bucketManagement)
    {
        var management = managementAvailable
            ? new SupabaseCapabilitySupport(SupabaseCapabilityLevel.Full)
            : new SupabaseCapabilitySupport(SupabaseCapabilityLevel.None, managementLimitation);

        return new Dictionary<SupabaseCapability, SupabaseCapabilitySupport>
        {
            [SupabaseCapability.ObjectStorage] = new(SupabaseCapabilityLevel.Full),
            [SupabaseCapability.PresignedGet] = new(SupabaseCapabilityLevel.Full),
            [SupabaseCapability.SignedUpload] = new(SupabaseCapabilityLevel.Partial, SignedUploadLimitation),
            [SupabaseCapability.PublicUrl] = new(SupabaseCapabilityLevel.Full),
            [SupabaseCapability.ImageTransform] = new(SupabaseCapabilityLevel.Full),
            [SupabaseCapability.BucketManagement] = bucketManagement,
            [SupabaseCapability.ProjectProvisioning] = management,
            [SupabaseCapability.ProjectKeys] = management,
            [SupabaseCapability.ProjectPause] = management,
            [SupabaseCapability.RealtimeBroadcast] = new(SupabaseCapabilityLevel.None, RealtimeLimitation),
        };
    }
}
