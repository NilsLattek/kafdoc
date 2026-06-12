using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Kafdoc.ArchitectureTest.ArchitectureModel;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Architecture tests that enforce naming conventions across the solution.
/// </summary>
public class NamingConventionTests
{
    [Fact]
    public void Mappers_end_with_mapper_suffix()
    {
        // Arrange
        IArchRule rule = Classes().That()
            .ResideInNamespace("Kafdoc.Application.Mapper")
            .Should().HaveNameEndingWith("Mapper");

        // Act + Assert
        rule.Check(Architecture);
    }
}
