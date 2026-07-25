// -----------------------------------------------------------------------
// <copyright file="JwtSigner.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Compendium.Adapters.Supabase.IntegrationTests.Fixtures;

/// <summary>
/// Minimal HS256 JWT signer used to mint the <c>service_role</c> / <c>anon</c> tokens the
/// storage-api container validates against a fixed <c>JWT_SECRET</c>. Avoids pulling a JWT
/// library into the integration tests for a one-line token.
/// </summary>
internal static class JwtSigner
{
    public static string CreateRoleToken(string secret, string role, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["role"] = role,
            ["iss"] = "supabase-demo",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
        };

        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Encode(object value) =>
        Base64Url(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
