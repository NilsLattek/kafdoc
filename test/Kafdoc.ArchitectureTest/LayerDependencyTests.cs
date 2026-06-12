using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Kafdoc.ArchitectureTest.ArchitectureModel;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Verifies the DDD layer dependency direction: Domain.Common and Domain have no
/// outbound dependencies on higher layers, and neither Application nor
/// Infrastructure depends on each other or on Web.
/// </summary>
public class LayerDependencyTests
{
    [Fact]
    public void Domain_common_does_not_depend_on_other_layers()
    {
        // Arrange
        IArchRule rule = Types().That().Are(DomainCommonLayer)
            .Should().NotDependOnAny(DomainLayer)
            .AndShould().NotDependOnAny(ApplicationLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(WebLayer);

        // Act + Assert
        rule.Check(Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_application_infrastructure_or_web()
    {
        // Arrange
        IArchRule rule = Types().That().Are(DomainLayer)
            .Should().NotDependOnAny(ApplicationLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(WebLayer);

        // Act + Assert
        rule.Check(Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web()
    {
        // Arrange
        IArchRule rule = Types().That().Are(ApplicationLayer)
            .Should().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(WebLayer);

        // Act + Assert
        rule.Check(Architecture);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_application_or_web()
    {
        // Arrange
        IArchRule rule = Types().That().Are(InfrastructureLayer)
            .Should().NotDependOnAny(ApplicationLayer)
            .AndShould().NotDependOnAny(WebLayer);

        // Act + Assert
        rule.Check(Architecture);
    }
}
