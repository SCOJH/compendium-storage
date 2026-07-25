// -----------------------------------------------------------------------
// <copyright file="SupabaseOptionsValidator.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Supabase.Options;

/// <summary>
/// Validates the cross-field rule that DataAnnotations cannot express : exactly one
/// of <see cref="SupabaseOptions.ServiceRoleKey"/> / <see cref="SupabaseOptions.AnonKey"/>
/// must be supplied. <see cref="SupabaseOptions.Url"/> and <see cref="SupabaseOptions.Bucket"/>
/// are covered by DataAnnotations validation.
/// </summary>
public sealed class SupabaseOptionsValidator : IValidateOptions<SupabaseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SupabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasServiceRole = !string.IsNullOrWhiteSpace(options.ServiceRoleKey);
        var hasAnon = !string.IsNullOrWhiteSpace(options.AnonKey);

        if (hasServiceRole == hasAnon)
        {
            return ValidateOptionsResult.Fail(
                "Exactly one of 'ServiceRoleKey' or 'AnonKey' must be configured under "
                + $"'{SupabaseOptions.SectionName}' (found "
                + (hasServiceRole ? "both" : "neither") + ").");
        }

        return ValidateOptionsResult.Success;
    }
}
