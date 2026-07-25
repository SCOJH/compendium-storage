// -----------------------------------------------------------------------
// <copyright file="ISupabaseAdmin.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Supabase.Connections;
using Compendium.Adapters.Supabase.Management.Buckets;
using Compendium.Adapters.Supabase.Management.Projects;

namespace Compendium.Adapters.Supabase.Management;

/// <summary>
/// The Supabase control-plane facade. A stateless singleton : every method takes an
/// explicit <see cref="SupabaseConnection"/> so one instance serves any number of
/// organizations / projects. All methods return the Result pattern and never throw for
/// control flow ; each gates on the connection's capabilities
/// (<see cref="Compendium.Adapters.Supabase.Capabilities.SupabaseCapabilities.For"/>) so a
/// keys-only or self-hosted connection fails uniformly rather than making a doomed call.
/// </summary>
/// <remarks>
/// <para>
/// Two planes are reached through the one facade :
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Cloud Management API</b> (<c>https://api.supabase.com</c>, Bearer personal access
///     token) — project create / get / delete and project API keys.
///   </description></item>
///   <item><description>
///     <b>Storage admin API</b> (project plane, <c>service_role</c> key) — bucket create /
///     list / delete. Works on both Cloud and self-hosted.
///   </description></item>
/// </list>
/// <para>
/// <see cref="AttachProjectAsync"/> is the bring-your-own / self-hosted escape hatch : it
/// probes an existing project's health and credential instead of provisioning one.
/// </para>
/// </remarks>
public interface ISupabaseAdmin
{
    /// <summary>
    /// Creates a Supabase Cloud project (<c>POST /v1/projects</c>). Provisioning is
    /// asynchronous upstream, so the returned project starts in
    /// <see cref="SupabaseProjectStatus.Provisioning"/> ; poll <see cref="GetProjectAsync"/>
    /// until <see cref="SupabaseProjectStatus.Active"/>.
    /// </summary>
    /// <param name="connection">A cloud connection carrying a management token.</param>
    /// <param name="spec">The project inputs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created project, or a failure.</returns>
    Task<Result<SupabaseProject>> CreateProjectAsync(
        SupabaseConnection connection,
        SupabaseProjectSpec spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a project's current state (<c>GET /v1/projects/{ref}</c>), mapping the raw
    /// upstream status onto <see cref="SupabaseProjectStatus"/>.
    /// </summary>
    /// <param name="connection">A cloud connection carrying a management token.</param>
    /// <param name="projectRef">The project ref.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The project, <c>Supabase.ProjectNotFound</c>, or a failure.</returns>
    Task<Result<SupabaseProject>> GetProjectAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a project (<c>DELETE /v1/projects/{ref}</c>). Idempotent : a missing project
    /// (<c>404</c>) is treated as success.
    /// </summary>
    /// <param name="connection">A cloud connection carrying a management token.</param>
    /// <param name="projectRef">The project ref.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or a failure.</returns>
    Task<Result> DeleteProjectAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a project's endpoint and API keys (<c>GET /v1/projects/{ref}/api-keys</c>).
    /// </summary>
    /// <param name="connection">A cloud connection carrying a management token.</param>
    /// <param name="projectRef">The project ref.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The keys, <c>Supabase.ProjectNotFound</c>, <c>Supabase.ProjectNotReady</c> (keys not
    /// yet provisioned), or a failure.
    /// </returns>
    Task<Result<SupabaseProjectKeys>> GetProjectKeysAsync(
        SupabaseConnection connection,
        string projectRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an existing bring-your-own / self-hosted project by probing its health and
    /// credential (no provisioning). The result is normalized to a self-hosted, active
    /// <see cref="SupabaseProject"/>.
    /// </summary>
    /// <param name="connection">
    /// A connection with a project <c>service_role</c>/<c>anon</c> key and a
    /// <see cref="SupabaseConnection.ProjectUrl"/> or
    /// <see cref="SupabaseConnection.StorageUrl"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The attached project, or <c>Supabase.AttachFailed</c>.</returns>
    Task<Result<SupabaseProject>> AttachProjectAsync(
        SupabaseConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a storage bucket (<c>POST /bucket</c>). Requires a <c>service_role</c> key ;
    /// works on both cloud and self-hosted planes.
    /// </summary>
    /// <param name="connection">A connection carrying a <c>service_role</c> key and a project/storage URL.</param>
    /// <param name="spec">The bucket inputs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created bucket, <c>Storage.ConflictExists</c>, or a failure.</returns>
    Task<Result<SupabaseBucket>> CreateBucketAsync(
        SupabaseConnection connection,
        SupabaseBucketSpec spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the storage buckets (<c>GET /bucket</c>). Requires a <c>service_role</c> key.
    /// </summary>
    /// <param name="connection">A connection carrying a <c>service_role</c> key and a project/storage URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The buckets, or a failure.</returns>
    Task<Result<IReadOnlyList<SupabaseBucket>>> ListBucketsAsync(
        SupabaseConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a storage bucket (<c>DELETE /bucket/{id}</c>). Requires a <c>service_role</c>
    /// key ; idempotent (a missing bucket is treated as success).
    /// </summary>
    /// <param name="connection">A connection carrying a <c>service_role</c> key and a project/storage URL.</param>
    /// <param name="bucketId">The bucket id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or a failure.</returns>
    Task<Result> DeleteBucketAsync(
        SupabaseConnection connection,
        string bucketId,
        CancellationToken cancellationToken = default);
}
