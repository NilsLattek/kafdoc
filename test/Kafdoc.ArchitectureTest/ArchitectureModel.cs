using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Kafdoc.ArchitectureTest;

/// <summary>
/// Loads the One Team layer assemblies once and exposes a type provider per layer.
/// Each assembly is resolved through a stable anchor type rather than a string name.
/// </summary>
internal static class ArchitectureModel
{
    private static readonly System.Reflection.Assembly DomainCommonAssembly =
        typeof(Kafdoc.Domain.Common.Dummy).Assembly;

    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(Kafdoc.Domain.Kafka.IKafkaClusterReader).Assembly;

    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(Kafdoc.Application.Services.ITopicQueryService).Assembly;

    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(Kafdoc.Infrastructure.ConfluentKafkaClusterReader).Assembly;

    private static readonly System.Reflection.Assembly WebAssembly =
        typeof(Program).Assembly;

    /// <summary>The loaded architecture spanning all five layers.</summary>
    public static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            DomainCommonAssembly,
            DomainAssembly,
            ApplicationAssembly,
            InfrastructureAssembly,
            WebAssembly)
        .Build();

    /// <summary>Types in the Domain.Common layer.</summary>
    public static readonly IObjectProvider<IType> DomainCommonLayer =
        Types().That().ResideInAssembly(DomainCommonAssembly).As("Domain.Common layer");

    /// <summary>Types in the Domain layer.</summary>
    public static readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInAssembly(DomainAssembly).As("Domain layer");

    /// <summary>Types in the Application layer.</summary>
    public static readonly IObjectProvider<IType> ApplicationLayer =
        Types().That().ResideInAssembly(ApplicationAssembly).As("Application layer");

    /// <summary>Types in the Infrastructure layer.</summary>
    public static readonly IObjectProvider<IType> InfrastructureLayer =
        Types().That().ResideInAssembly(InfrastructureAssembly).As("Infrastructure layer");

    /// <summary>Types in the Web layer.</summary>
    public static readonly IObjectProvider<IType> WebLayer =
        Types().That().ResideInAssembly(WebAssembly).As("Web layer");
}
