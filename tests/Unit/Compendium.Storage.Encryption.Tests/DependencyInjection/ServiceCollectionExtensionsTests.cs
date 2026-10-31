// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Storage;
using Compendium.Abstractions.Storage.Encryption;
using Compendium.Multitenancy;
using Compendium.Storage.Encryption.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Storage.Encryption.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumStorageEncryption_WithDependencies_ResolvesEncryptingObjectStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IObjectStore>());
        services.AddSingleton(Substitute.For<IKeyEncryptionKeyProvider>());
        services.AddSingleton(Substitute.For<ITenantContextAccessor>());

        // Act
        services.AddCompendiumStorageEncryption();
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IEncryptedObjectStore>().Should().BeOfType<EncryptingObjectStore>();
    }

    [Fact]
    public void AddCompendiumStorageEncryption_NullServices_Throws()
    {
        // Act
        var act = () => ServiceCollectionExtensions.AddCompendiumStorageEncryption(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
