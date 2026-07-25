// -----------------------------------------------------------------------
// <copyright file="SupabaseStorageFixture.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Compendium.Adapters.Supabase.Storage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;
using Xunit;

namespace Compendium.Adapters.Supabase.IntegrationTests.Fixtures;

/// <summary>
/// Boots <c>supabase/postgres</c> + <c>supabase/storage-api</c> on a shared network with a
/// fixed <c>JWT_SECRET</c>, mints a <c>service_role</c> JWT, points the adapter's
/// <c>StorageUrl</c> directly at the storage-api container (no Kong), and pre-creates the
/// test bucket. Reused by the integration tests via <see cref="IAsyncLifetime"/>.
/// </summary>
public sealed class SupabaseStorageFixture : IAsyncLifetime
{
    private const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";
    private const int StoragePort = 5000;

    private INetwork _network = null!;
    private PostgreSqlContainer _postgres = null!;
    private IContainer _storage = null!;

    /// <summary>Direct storage-api URL (no Kong), e.g. <c>http://localhost:32768</c>.</summary>
    public string StorageUrl { get; private set; } = string.Empty;

    /// <summary>Minted <c>service_role</c> JWT signed with the fixed secret.</summary>
    public string ServiceRoleKey { get; private set; } = string.Empty;

    /// <summary>The bucket created during fixture initialisation.</summary>
    public string Bucket { get; } = "assets";

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().Build();

        // Plain postgres — single-phase init, so pg_isready reliably means the TCP port is
        // accepting connections (supabase/postgres restarts mid-init, racing storage-api,
        // which fails fast on its single DB-connection attempt). storage-api installs its
        // own roles + storage schema as the superuser (DB_INSTALL_ROLES=true).
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("db")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _postgres.StartAsync().ConfigureAwait(false);

        ServiceRoleKey = JwtSigner.CreateRoleToken(JwtSecret, "service_role", TimeSpan.FromHours(2));
        var anonKey = JwtSigner.CreateRoleToken(JwtSecret, "anon", TimeSpan.FromHours(2));

        _storage = new ContainerBuilder()
            .WithImage("supabase/storage-api:v1.11.13")
            .WithNetwork(_network)
            .WithEnvironment("ANON_KEY", anonKey)
            .WithEnvironment("SERVICE_KEY", ServiceRoleKey)
            .WithEnvironment("PGRST_JWT_SECRET", JwtSecret)
            .WithEnvironment("AUTH_JWT_SECRET", JwtSecret)
            .WithEnvironment("DATABASE_URL", "postgres://postgres:postgres@db:5432/postgres")
            .WithEnvironment("DB_SUPER_USER", "postgres")
            .WithEnvironment("DB_INSTALL_ROLES", "true")
            .WithEnvironment("FILE_SIZE_LIMIT", "52428800")
            .WithEnvironment("STORAGE_BACKEND", "file")
            .WithEnvironment("FILE_STORAGE_BACKEND_PATH", "/var/lib/storage")
            .WithEnvironment("TENANT_ID", "stub")
            .WithEnvironment("REGION", "stub")
            .WithEnvironment("GLOBAL_S3_BUCKET", "stub")
            .WithEnvironment("ENABLE_IMAGE_TRANSFORMATION", "false")
            .WithPortBinding(StoragePort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(StoragePort).ForPath("/status")))
            .Build();
        await _storage.StartAsync().ConfigureAwait(false);

        // A bare storage-api serves its routes at the ROOT (POST /bucket, GET /object/...).
        // The /storage/v1 prefix is added by the Kong gateway, which we bypass here.
        StorageUrl = $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(StoragePort)}";
        await CreateBucketAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_storage is not null)
        {
            await _storage.DisposeAsync().ConfigureAwait(false);
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().ConfigureAwait(false);
        }

        if (_network is not null)
        {
            await _network.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CreateBucketAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", ServiceRoleKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceRoleKey);
        using var response = await http
            .PostAsJsonAsync($"{StorageUrl}/bucket", new { id = Bucket, name = Bucket, @public = false })
            .ConfigureAwait(false);

        // 200 = created ; 409 = already exists (idempotent across retried fixtures).
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Failed to create bucket '{Bucket}' ({(int)response.StatusCode}): {body}");
        }
    }
}
