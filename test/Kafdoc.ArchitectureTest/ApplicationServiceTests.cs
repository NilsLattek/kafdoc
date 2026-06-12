using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Kafdoc.ArchitectureTest.ArchitectureModel;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Verifies that concrete application-service implementations are never public,
/// keeping the public surface limited to service interfaces.
/// </summary>
public class ApplicationServiceTests
{
    [Fact]
    public void Service_implementations_are_not_public()
    {
        // Arrange
        IArchRule rule = Classes().That()
            .ResideInNamespace("Kafdoc.Application.Services")
            .And().HaveNameEndingWith("Service")
            .Should().NotBePublic();

        // Act + Assert
        rule.Check(Architecture);
    }
}
