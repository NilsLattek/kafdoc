using ArchUnitNET.Domain;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Guards the shared <see cref="ArchitectureModel"/> by asserting every layer
/// provider resolves to at least one type, so the other rules cannot pass vacuously.
/// </summary>
public class ArchitectureModelTests
{
    [Fact]
    public void Architecture_loads_all_five_layer_assemblies()
    {
        // Arrange
        IObjectProvider<IType>[] layers =
        [
            ArchitectureModel.DomainCommonLayer,
            ArchitectureModel.DomainLayer,
            ArchitectureModel.ApplicationLayer,
            ArchitectureModel.InfrastructureLayer,
            ArchitectureModel.WebLayer,
        ];

        // Act
        var typeCounts = layers
            .Select(layer => layer.GetObjects(ArchitectureModel.Architecture).Count())
            .ToList();

        // Assert
        Assert.All(typeCounts, count => Assert.True(count > 0));
    }
}
