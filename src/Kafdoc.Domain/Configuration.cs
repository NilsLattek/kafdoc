using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;

namespace Kafdoc.Domain;

/// <summary>
/// Dependency injection registrations for the Domain layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Domain-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureDomain(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClusterFilterOptions>()
            .Bind(configuration.GetSection(ClusterFilterOptions.SectionName))
            .PostConfigure(options =>
            {
                var saslUsername = configuration["Kafka:SaslUsername"];
                if (!string.IsNullOrWhiteSpace(saslUsername))
                {
                    options.ExcludedUsers = [.. options.ExcludedUsers, saslUsername];
                }
            });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClusterFilterOptions>>().Value);

        services.AddSingleton<RawClusterDataFilter>();
        services.AddSingleton<Kafdoc.Domain.Graph.ClusterGraphBuilder>();
    }
}
