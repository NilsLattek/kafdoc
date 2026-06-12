using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kafdoc.Application;

/// <summary>
/// Dependency injection registrations for the Application layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Application-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Snapshot, refresh, and query services are registered in Tasks 5 and 6.
    }
}
