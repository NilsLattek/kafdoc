using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        // Graph builder is registered in Task 4.
    }
}
