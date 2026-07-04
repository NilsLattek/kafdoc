using Kafdoc.Application.Services;
using Kafdoc.Domain.Documentation;

using NSubstitute;

namespace Kafdoc.ApplicationTest.Services;

public class IntroductionQueryServiceTests
{
    [Fact]
    public void GetIntroduction_returns_source_content()
    {
        // Arrange
        var source = Substitute.For<IIntroductionSource>();
        source.Read().Returns("# Hello");
        var service = new IntroductionQueryService(source);

        // Act
        var result = service.GetIntroduction();

        // Assert
        Assert.Equal("# Hello", result);
    }

    [Fact]
    public void GetIntroduction_returns_null_when_source_null()
    {
        // Arrange
        var source = Substitute.For<IIntroductionSource>();
        source.Read().Returns((string?)null);
        var service = new IntroductionQueryService(source);

        // Act
        var result = service.GetIntroduction();

        // Assert
        Assert.Null(result);
    }
}
