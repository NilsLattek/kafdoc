using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;

using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Kafdoc.ArchitectureTest.ArchitectureModel;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Verifies that the Web layer does not bypass the Application layer
/// and reach directly into Domain or Infrastructure types.
/// The sole exemption is the composition root (<c>Program</c>), which
/// legitimately calls the <c>Configure*</c> DI extension methods.
/// </summary>
public class WebIsolationTests
{
    [Fact]
    public void Web_reaches_data_only_through_application_services()
    {
        // Arrange — Program is the composition root; it alone may wire up
        // Infrastructure/Domain via the Configure* DI extensions.
        IArchRule rule = Types().That().Are(WebLayer)
            .And().DoNotHaveName("Program")
            .Should().NotDependOnAny(DomainLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer);

        // Act + Assert
        rule.Check(Architecture);
    }
}
