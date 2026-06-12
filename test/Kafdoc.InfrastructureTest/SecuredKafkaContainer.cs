using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Kafdoc.InfrastructureTest;

/// <summary>
/// A single-node KRaft Kafka broker secured with SASL_PLAINTEXT (SCRAM-SHA-512) and the
/// standard authorizer. The <c>admin</c> super-user is provisioned via broker JAAS so the
/// admin client can authenticate before any other SCRAM users are created.
/// </summary>
internal sealed class SecuredKafkaContainer
{
    private const int Port = 9092;
    private readonly IContainer _container;

    /// <summary>Builds the secured KRaft broker container (not yet started).</summary>
    public SecuredKafkaContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("apache/kafka:3.8.0")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_LISTENERS", "SASL://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "SASL://localhost:9092")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "SASL:SASL_PLAINTEXT,CONTROLLER:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "SASL")
            .WithEnvironment("KAFKA_SASL_ENABLED_MECHANISMS", "SCRAM-SHA-512")
            .WithEnvironment("KAFKA_SASL_MECHANISM_INTER_BROKER_PROTOCOL", "SCRAM-SHA-512")
            .WithEnvironment("KAFKA_AUTHORIZER_CLASS_NAME", "org.apache.kafka.metadata.authorizer.StandardAuthorizer")
            .WithEnvironment("KAFKA_SUPER_USERS", "User:admin")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            // Provision the admin super-user's SCRAM credential at format time.
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_BOOTSTRAP_SERVERS", "localhost:9093")
            .WithEnvironment(
                "KAFKA_LISTENER_NAME_SASL_SCRAM-SHA-512_SASL_JAAS_CONFIG",
                "org.apache.kafka.common.security.scram.ScramLoginModule required;")
            .WithEnvironment(
                "KAFKA_OPTS",
                "-Dorg.apache.kafka.disallowed.login.modules=")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();
    }

    /// <summary>The host bootstrap servers once started.</summary>
    public string BootstrapServers => $"localhost:{_container.GetMappedPublicPort(Port)}";

    /// <summary>Starts the broker container.</summary>
    /// <param name="ct">A token to cancel the start.</param>
    public Task StartAsync(CancellationToken ct) => _container.StartAsync(ct);

    /// <summary>Stops and removes the broker container.</summary>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Runs a kafka admin shell command inside the broker container.</summary>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="ct">A token to cancel execution.</param>
    public Task<ExecResult> ExecAsync(IList<string> command, CancellationToken ct) =>
        _container.ExecAsync(command, ct);
}
