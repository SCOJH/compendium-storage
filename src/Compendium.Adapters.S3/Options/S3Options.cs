// -----------------------------------------------------------------------
// <copyright file="S3Options.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace Compendium.Adapters.S3.Options;

/// <summary>
/// Configuration for the S3 object-store adapter.
/// </summary>
/// <remarks>
/// <para>
/// One adapter, many providers : AWS S3, Cloudflare R2, MinIO, Backblaze B2, Wasabi.
/// Point at a non-AWS endpoint via <see cref="ServiceUrl"/> + <see cref="ForcePathStyle"/>.
/// </para>
/// <para>
/// Credentials are optional. When omitted, the AWS SDK's standard credential chain
/// kicks in (env vars, shared profile, EC2 instance profile, EKS pod identity, ...).
/// Prefer IAM roles in production.
/// </para>
/// </remarks>
public sealed class S3Options
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Compendium:Adapters:S3";

    /// <summary>
    /// Default multipart-upload threshold : 8 MiB. Uploads larger than this go
    /// through <c>TransferUtility</c> for parallel multipart upload.
    /// </summary>
    public const long DefaultMultipartThresholdBytes = 8L * 1024L * 1024L;

    /// <summary>
    /// AWS access key id. Leave null to use the SDK's default credential chain.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// AWS secret access key. Leave null to use the SDK's default credential chain.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// AWS region (e.g. <c>us-east-1</c>). Required when no <see cref="ServiceUrl"/>
    /// is provided.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Custom service endpoint URL. Set this for Cloudflare R2, MinIO, Backblaze B2,
    /// Wasabi, or any other S3-compatible provider. Leave null for AWS S3.
    /// </summary>
    [Url]
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// When true, addresses objects via <c>https://endpoint/bucket/key</c> rather
    /// than the virtual-hosted-style <c>https://bucket.endpoint/key</c>. Required
    /// for MinIO. Optional for Cloudflare R2.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Default bucket. Required.
    /// </summary>
    [Required]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Multipart-upload threshold, in bytes. Default 8 MiB.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long MultipartThresholdBytes { get; set; } = DefaultMultipartThresholdBytes;

    /// <summary>
    /// Default lifetime for presigned URLs. Default 15 minutes.
    /// </summary>
    public TimeSpan DefaultPresignedUrlExpiry { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Server-side encryption algorithm. Default <see cref="ServerSideEncryption.None"/>.
    /// </summary>
    public ServerSideEncryption ServerSideEncryption { get; set; } = ServerSideEncryption.None;

    /// <summary>
    /// KMS key id. Required when <see cref="ServerSideEncryption"/> is
    /// <see cref="ServerSideEncryption.AwsKms"/>.
    /// </summary>
    public string? KmsKeyId { get; set; }
}

/// <summary>
/// Server-side encryption algorithm.
/// </summary>
public enum ServerSideEncryption
{
    /// <summary>
    /// No server-side encryption.
    /// </summary>
    None = 0,

    /// <summary>
    /// SSE-S3 (AES-256), the most common option.
    /// </summary>
    Aes256 = 1,

    /// <summary>
    /// SSE-KMS using a customer-managed AWS KMS key.
    /// </summary>
    AwsKms = 2,
}
