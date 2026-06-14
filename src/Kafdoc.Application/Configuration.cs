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
        services.AddOptions<Snapshot.RefreshOptions>()
            .Bind(configuration.GetSection(Snapshot.RefreshOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Snapshot.ISnapshotStore, Snapshot.SnapshotStore>();
        services.AddScoped<Snapshot.IClusterRefreshService, Snapshot.ClusterRefreshService>();

        services.AddSingleton<Services.ITopicQueryService, Services.TopicQueryService>();
        services.AddSingleton<Services.IUserQueryService, Services.UserQueryService>();
        services.AddSingleton<Services.ISnapshotStatusService, Services.SnapshotStatusService>();

        services.AddHostedService<Snapshot.ClusterRefreshHostedService>();
    }
}
