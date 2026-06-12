using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Kafdoc.ArchitectureTest.ArchitectureModel;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Verifies that concrete application-service implementations are never public,
/// keeping the public surface limited to the <c>IChartAppService</c> and
/// <c>IOrgNodeAppService</c> interfaces.
/// </summary>
public class ApplicationServiceTests
{
    [Fact]
    public void App_service_implementations_are_not_public()
    {
        // Arrange
        IArchRule rule = Classes().That()
            .ResideInNamespace("Kafdoc.Application.Services")
            .And().HaveNameEndingWith("AppService")
            .Should().NotBePublic();

        // Act + Assert
        rule.Check(Architecture);
    }
}
