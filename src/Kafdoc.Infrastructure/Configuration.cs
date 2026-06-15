using Confluent.Kafka;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Documentation;
using Kafdoc.Domain.Kafka;
using Kafdoc.Infrastructure.Documentation;
using Kafdoc.Infrastructure.Kafka;

namespace Kafdoc.Infrastructure;

/// <summary>Dependency injection registrations for the Infrastructure layer.</summary>
public static class Configuration
{
    /// <summary>
    /// Registers the Kafka admin client and cluster reader.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaConnectionOptions>()
            .Bind(configuration.GetSection(KafkaConnectionOptions.SectionName));

        services.AddSingleton<IAdminClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaConnectionOptions>>().Value;
            var config = new AdminClientConfig
            {
                BootstrapServers = options.BootstrapServers,
                SecurityProtocol = Enum.Parse<SecurityProtocol>(options.SecurityProtocol, ignoreCase: true),
                SaslMechanism = Enum.Parse<SaslMechanism>(options.SaslMechanism, ignoreCase: true),
                SaslUsername = options.SaslUsername,
                SaslPassword = options.SaslPassword,
                SslCaLocation = options.SslCaLocation,
            };
            return new AdminClientBuilder(config).Build();
        });

        services.AddOptions<DocumentationOptions>()
            .Bind(configuration.GetSection(DocumentationOptions.SectionName));
        services.AddSingleton<IDocumentationStore, FileDocumentationStore>();

        services.AddSingleton<IKafkaClusterReader, ConfluentKafkaClusterReader>();
    }
}
