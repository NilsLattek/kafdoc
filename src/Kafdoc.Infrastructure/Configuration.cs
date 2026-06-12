using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kafdoc.Infrastructure;

/// <summary>
/// Dependency injection registrations for the Infrastructure layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Infrastructure-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Kafka admin client and reader are registered in Task 7.
    }
}
