using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;
using Kafdoc.Infrastructure.Kafka;

namespace Kafdoc.InfrastructureTest;

/// <summary>
/// Integration tests that drive <see cref="ConfluentKafkaClusterReader"/> against a real
/// secured KRaft broker started via Testcontainers.
/// </summary>
public sealed class ConfluentKafkaClusterReaderTests(ITestOutputHelper testOutputHelper) : KafkaTest(testOutputHelper), IAsyncLifetime
{
    private IAdminClient _admin = null!;

    /// <summary>Starts the broker, connects an admin client, and seeds test data.</summary>
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _admin = new AdminClientBuilder(AdminConfig()).Build();
        await SeedAsync();
    }

    /// <summary>Disposes the admin client and tears down the broker.</summary>
    public async ValueTask DisposeAsync()
    {
        _admin?.Dispose();
    }

    private AdminClientConfig AdminConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        SecurityProtocol = SecurityProtocol.SaslPlaintext,
        SaslMechanism = SaslMechanism.ScramSha512,
        SaslUsername = "admin",
        SaslPassword = "admin-secret",
    };

    private async Task SeedAsync()
    {
        await _admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = "orders", NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = "payments", NumPartitions = 1, ReplicationFactor = 1 },
        ]);

        await _admin.CreateAclsAsync(
        [
            new AclBinding
            {
                Pattern = new ResourcePattern
                {
                    Type = ResourceType.Topic,
                    Name = "orders",
                    ResourcePatternType = ResourcePatternType.Literal,
                },
                Entry = new AccessControlEntry
                {
                    Principal = "User:svc-orders",
                    Host = "*",
                    Operation = AclOperation.Write,
                    PermissionType = AclPermissionType.Allow,
                },
            },
        ]);

        await _admin.AlterUserScramCredentialsAsync(
        [
            new UserScramCredentialUpsertion
            {
                User = "svc-orders",
                ScramCredentialInfo = new ScramCredentialInfo
                {
                    Mechanism = ScramMechanism.ScramSha512,
                    Iterations = 4096,
                },
                Password = System.Text.Encoding.UTF8.GetBytes("svc-orders-secret"),
            },
        ]);

        // Two consumer groups with committed offsets. Listing offsets for more than one group
        // is what regressed (Confluent.Kafka only allows one group per call), so seed two.
        await CommitOffsetAsync("orders-consumers", "orders");
        await CommitOffsetAsync("payments-consumers", "payments");
    }

    /// <summary>Registers a consumer group by committing an offset on a topic partition.</summary>
    private async Task CommitOffsetAsync(string groupId, string topic)
    {
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            SecurityProtocol = SecurityProtocol.SaslPlaintext,
            SaslMechanism = SaslMechanism.ScramSha512,
            SaslUsername = "admin",
            SaslPassword = "admin-secret",
            GroupId = groupId,
            EnableAutoCommit = false,
        }).Build();

        consumer.Commit([new TopicPartitionOffset(topic, 0, new Offset(1))]);
        consumer.Close();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReadAsync_returns_seeded_topic_acl_and_scram_user()
    {
        // Arrange
        var reader = new ConfluentKafkaClusterReader(
            _admin,
            Options.Create(new KafkaConnectionOptions { RequestTimeout = TimeSpan.FromSeconds(30) }));

        // Act
        var data = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(data.Topics, t => string.Equals(t.Name, "orders", StringComparison.Ordinal));
        Assert.Contains(data.Acls, a =>
            string.Equals(a.Principal, "User:svc-orders", StringComparison.Ordinal)
            && a.ResourceType == KafkaResourceType.Topic
            && string.Equals(a.ResourceName, "orders", StringComparison.Ordinal)
            && a.Operation == KafkaAclOperation.Write
            && a.Permission == KafkaAclPermission.Allow);
        Assert.Contains(data.ScramUsers, u => string.Equals(u.Principal, "User:svc-orders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_lists_offsets_for_multiple_consumer_groups()
    {
        // Arrange
        var reader = new ConfluentKafkaClusterReader(
            _admin,
            Options.Create(new KafkaConnectionOptions { RequestTimeout = TimeSpan.FromSeconds(30) }));

        // Act
        var data = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(data.ConsumerGroups, g =>
            string.Equals(g.GroupId, "orders-consumers", StringComparison.Ordinal)
            && g.ConsumedTopics.Contains("orders", StringComparer.Ordinal));
        Assert.Contains(data.ConsumerGroups, g =>
            string.Equals(g.GroupId, "payments-consumers", StringComparison.Ordinal)
            && g.ConsumedTopics.Contains("payments", StringComparer.Ordinal));
    }
}
