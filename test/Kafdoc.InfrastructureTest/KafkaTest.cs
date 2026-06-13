using System.Text;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using Testcontainers.Xunit;

namespace Kafdoc.InfrastructureTest;

/// <summary>
/// xUnit base fixture for a single-node KRaft Kafka broker secured with SASL_PLAINTEXT
/// (SCRAM-SHA-512) and the standard authorizer.
/// <para>
/// Built from a generic <see cref="ContainerBuilder"/> rather than the opinionated
/// <c>KafkaBuilder</c>, which hardcodes PLAINTEXT listeners and cannot be reconfigured for SASL.
/// Three listeners are used: <c>CONTROLLER</c> (KRaft, PLAINTEXT), <c>BROKER</c> (inter-broker,
/// PLAINTEXT, advertised inside the container) and <c>EXTERNAL</c> (SASL_PLAINTEXT, advertised on
/// the host-mapped port for the admin client).
/// </para>
/// <para>
/// Two things the apache/kafka image does not do for us are handled in <see cref="BuildStartupScript"/>:
/// the admin super-user's SCRAM credential is seeded at storage-format time via <c>--add-scram</c>
/// (otherwise the admin client has nothing to authenticate against), and the <c>EXTERNAL</c>
/// advertised listener is rewritten to the random host-mapped port (only known once the container
/// is running) so host clients reconnect to the right address instead of an unreachable 9092.
/// </para>
/// </summary>
public class KafkaTest(ITestOutputHelper testOutputHelper)
    : ContainerTest<ContainerBuilder, IContainer>(testOutputHelper)
{
    /// <summary>The container-internal EXTERNAL (SASL) listener port, bound to a random host port.</summary>
    protected const int ExternalPort = 9092;

    /// <summary>The seeded admin super-user's SCRAM-SHA-512 password.</summary>
    protected const string AdminPassword = "admin-secret";

    /// <summary>A fixed cluster id so the storage format step is deterministic.</summary>
    private const string ClusterId = "5L6g3nShT-eMCtK--X86sw";

    /// <summary>Path of the startup script copied into the container once its host port is known.</summary>
    private const string StartupScriptPath = "/tmp/tc-start.sh";

    /// <summary>
    /// The host bootstrap servers once the broker is started. Uses <see cref="IContainer.Hostname"/>
    /// (honouring <c>TESTCONTAINERS_HOST_OVERRIDE</c>) rather than a hardcoded <c>localhost</c>, so
    /// it resolves correctly in docker-in-docker setups where published ports are reachable via the
    /// bridge gateway rather than the test process's loopback.
    /// </summary>
    protected string BootstrapServers => $"{Container.Hostname}:{Container.GetMappedPublicPort(ExternalPort)}";

    /// <summary>Configures the secured KRaft broker container.</summary>
    protected override ContainerBuilder Configure() =>
        new ContainerBuilder("apache/kafka:3.8.0")
            .WithPortBinding(ExternalPort, assignRandomHostPort: true)
            .WithEnvironment("CLUSTER_ID", ClusterId)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_BOOTSTRAP_SERVERS", "localhost:9093")
            .WithEnvironment("KAFKA_LISTENERS", "CONTROLLER://0.0.0.0:9093,BROKER://0.0.0.0:9094,EXTERNAL://0.0.0.0:9092")
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                "CONTROLLER:PLAINTEXT,BROKER:PLAINTEXT,EXTERNAL:SASL_PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "BROKER")
            .WithEnvironment("KAFKA_SASL_ENABLED_MECHANISMS", "SCRAM-SHA-512")
            .WithEnvironment("KAFKA_AUTHORIZER_CLASS_NAME", "org.apache.kafka.metadata.authorizer.StandardAuthorizer")
            // ANONYMOUS must be a super-user: the CONTROLLER and BROKER listeners are PLAINTEXT, so the
            // broker's own KRaft self-registration and inter-broker traffic arrive as User:ANONYMOUS.
            // Without this they are denied and the broker never finishes starting. The EXTERNAL SASL
            // listener still authenticates real users, so ACL enforcement under test is unaffected.
            .WithEnvironment("KAFKA_SUPER_USERS", "User:admin;User:ANONYMOUS")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_LOG_DIRS", "/tmp/kafka-logs")
            .WithEnvironment(
                "KAFKA_LISTENER_NAME_EXTERNAL_SCRAM-SHA-512_SASL_JAAS_CONFIG",
                "org.apache.kafka.common.security.scram.ScramLoginModule required;")
            .WithEnvironment("KAFKA_OPTS", "-Dorg.apache.kafka.disallowed.login.modules=")
            // Block on the startup script until the callback below copies it in with the mapped port.
            .WithEntrypoint(
                "/bin/bash",
                "-c",
                $"while [ ! -f {StartupScriptPath} ]; do sleep 0.1; done; exec /bin/bash {StartupScriptPath}")
            .WithStartupCallback((container, ct) =>
            {
                var script = BuildStartupScript(container.Hostname, container.GetMappedPublicPort(ExternalPort));
                return container.CopyAsync(
                    Encoding.UTF8.GetBytes(script),
                    StartupScriptPath,
                    uid: 0,
                    gid: 0,
                    fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute
                        | UnixFileModes.GroupRead | UnixFileModes.GroupExecute
                        | UnixFileModes.OtherRead | UnixFileModes.OtherExecute,
                    ct);
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"));

    /// <summary>
    /// Builds the in-container startup script. The apache/kafka image's <c>KafkaDockerWrapper setup</c>
    /// generates the KRaft <c>server.properties</c> from the <c>KAFKA_*</c> env vars and formats storage,
    /// but cannot seed a SCRAM credential. We override the EXTERNAL advertised listener with the host
    /// port, let the wrapper generate the config, then wipe and re-format the metadata log with
    /// <c>--add-scram</c> so the admin super-user exists before the broker starts.
    /// </summary>
    /// <param name="advertisedHost">The host the test client reaches the broker on (see <see cref="BootstrapServers"/>).</param>
    /// <param name="externalPort">The host port mapped to the container's EXTERNAL listener.</param>
    private static string BuildStartupScript(string advertisedHost, ushort externalPort) => $$"""
        set -e
        export KAFKA_ADVERTISED_LISTENERS="BROKER://localhost:9094,EXTERNAL://{{advertisedHost}}:{{externalPort}}"
        . /etc/kafka/docker/configureDefaults
        . /etc/kafka/docker/configure
        /opt/kafka/bin/kafka-run-class.sh kafka.docker.KafkaDockerWrapper setup \
          --default-configs-dir /etc/kafka/docker \
          --mounted-configs-dir /mnt/shared/config \
          --final-configs-dir /opt/kafka/config
        LOGDIR=$(grep '^log.dirs' /opt/kafka/config/server.properties | cut -d= -f2)
        LOGDIR=${LOGDIR:-/tmp/kafka-logs}
        rm -rf "${LOGDIR:?}"/*
        /opt/kafka/bin/kafka-storage.sh format -t "{{ClusterId}}" \
          -c /opt/kafka/config/server.properties \
          --add-scram "SCRAM-SHA-512=[name=admin,password={{AdminPassword}}]"
        exec /opt/kafka/bin/kafka-server-start.sh /opt/kafka/config/server.properties
        """;
}
