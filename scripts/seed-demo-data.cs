#:package Confluent.Kafka@2.14.2
#:property ManagePackageVersionsCentrally=false

// Seeds the local dev Kafka cluster (docker-compose, kafka:9092) with demo data so the
// Kafdoc UI has something to show: SCRAM users, topics, ACLs (WRITE/READ on topics, READ
// on consumer groups, plus LITERAL/PREFIXED/* patterns), a few produced messages, and
// committed consumer-group offsets. Idempotent: safe to run repeatedly.
//
// Run from the devcontainer:
//   dotnet run scripts/seed-demo-data.cs
//
// Override the connection with env vars if needed:
//   KAFKA_BOOTSTRAP (default kafka:9092), KAFKA_USER (admin), KAFKA_PASSWORD (admin-secret)

using System.Text;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

var bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";
var adminUser = Environment.GetEnvironmentVariable("KAFKA_USER") ?? "admin";
var adminPassword = Environment.GetEnvironmentVariable("KAFKA_PASSWORD") ?? "admin-secret";

var clientConfig = new ClientConfig
{
    BootstrapServers = bootstrap,
    SecurityProtocol = SecurityProtocol.SaslPlaintext,
    SaslMechanism = SaslMechanism.ScramSha512,
    SaslUsername = adminUser,
    SaslPassword = adminPassword,
};

Console.WriteLine($"Connecting to {bootstrap} as {adminUser} ...");

using var admin = new AdminClientBuilder(clientConfig).Build();

// ---------------------------------------------------------------------------
// Demo model
// ---------------------------------------------------------------------------

// SCRAM users (the principal becomes "User:<name>"). Password is irrelevant for the demo.
string[] users =
[
    "orders-service",
    "payments-service",
    "shipping-service",
    "analytics",
    "audit-reader",
];

// Topics with partition counts.
var topics = new (string Name, int Partitions)[]
{
    ("orders", 6),
    ("payments", 3),
    ("shipments", 3),
    ("inventory", 3),
    ("audit-events", 1),
    ("metrics.cpu", 2),
    ("metrics.memory", 2),
};

// ACLs. Type Literal/Prefixed; Topic vs Group; Operation Write/Read.
var acls = new List<AclBinding>
{
    // orders-service: produces orders, reads inventory
    TopicAcl("orders-service", "orders", AclOperation.Write),
    TopicAcl("orders-service", "inventory", AclOperation.Read),

    // payments-service: produces payments, reads orders, owns the payments-processors group
    TopicAcl("payments-service", "payments", AclOperation.Write),
    TopicAcl("payments-service", "orders", AclOperation.Read),
    GroupAcl("payments-service", "payments-processors", AclOperation.Read, ResourcePatternType.Literal),

    // shipping-service: produces shipments, reads payments + orders, owns shipping-workers group
    TopicAcl("shipping-service", "shipments", AclOperation.Write),
    TopicAcl("shipping-service", "payments", AclOperation.Read),
    TopicAcl("shipping-service", "orders", AclOperation.Read),
    GroupAcl("shipping-service", "shipping-workers", AclOperation.Read, ResourcePatternType.Literal),

    // analytics: reads ALL metrics.* topics (PREFIXED pattern), owns any analytics-* group (PREFIXED)
    TopicAcl("analytics", "metrics.", AclOperation.Read, ResourcePatternType.Prefixed),
    GroupAcl("analytics", "analytics-", AclOperation.Read, ResourcePatternType.Prefixed),

    // audit-reader: reads audit-events, owns audit-archivers group
    TopicAcl("audit-reader", "audit-events", AclOperation.Read),
    GroupAcl("audit-reader", "audit-archivers", AclOperation.Read, ResourcePatternType.Literal),
};

// Consumer groups and the topics they have committed offsets on (group -> topic edges).
var groupOffsets = new (string Group, string[] Topics)[]
{
    ("payments-processors", ["orders"]),
    ("shipping-workers", ["payments", "orders"]),
    ("analytics-metrics", ["metrics.cpu", "metrics.memory"]),
    ("audit-archivers", ["audit-events"]),
};

// ---------------------------------------------------------------------------
// Seed
// ---------------------------------------------------------------------------

await CreateUsersAsync();
await CreateTopicsAsync();
await CreateAclsAsync();
await ProduceSampleMessagesAsync();
await CommitGroupOffsetsAsync();

Console.WriteLine();
Console.WriteLine("Done. Refresh Kafdoc (or wait for the next refresh) to see the demo data.");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async Task CreateUsersAsync()
{
    Console.WriteLine($"\nCreating {users.Length} SCRAM users ...");
    var upsertions = users
        .Select(u => (UserScramCredentialAlteration)new UserScramCredentialUpsertion
        {
            User = u,
            ScramCredentialInfo = new ScramCredentialInfo
            {
                Mechanism = ScramMechanism.ScramSha512,
                Iterations = 4096,
            },
            Password = Encoding.UTF8.GetBytes($"{u}-secret"),
        })
        .ToList();

    try
    {
        await admin.AlterUserScramCredentialsAsync(upsertions);
        foreach (var u in users)
        {
            Console.WriteLine($"  + User:{u}");
        }
    }
    catch (KafkaException ex)
    {
        Console.WriteLine($"  ! SCRAM user creation: {ex.Message}");
    }
}

async Task CreateTopicsAsync()
{
    Console.WriteLine($"\nCreating {topics.Length} topics ...");
    var specs = topics
        .Select(t => new TopicSpecification { Name = t.Name, NumPartitions = t.Partitions, ReplicationFactor = 1 })
        .ToList();

    try
    {
        await admin.CreateTopicsAsync(specs);
        foreach (var t in topics)
        {
            Console.WriteLine($"  + {t.Name} ({t.Partitions} partitions)");
        }
    }
    catch (CreateTopicsException ex)
    {
        foreach (var r in ex.Results)
        {
            var state = r.Error.Code == ErrorCode.TopicAlreadyExists ? "exists" : r.Error.Reason;
            Console.WriteLine($"  . {r.Topic} ({state})");
        }
    }
}

async Task CreateAclsAsync()
{
    Console.WriteLine($"\nCreating {acls.Count} ACLs ...");
    try
    {
        await admin.CreateAclsAsync(acls);
        foreach (var a in acls)
        {
            Console.WriteLine(
                $"  + {a.Entry.Principal} {a.Entry.Operation} {a.Pattern.Type}:{a.Pattern.Name} ({a.Pattern.ResourcePatternType})");
        }
    }
    catch (CreateAclsException ex)
    {
        Console.WriteLine($"  ! ACL creation: {ex.Message}");
    }
}

async Task ProduceSampleMessagesAsync()
{
    Console.WriteLine("\nProducing sample messages ...");
    using var producer = new ProducerBuilder<string, string>(clientConfig).Build();

    foreach (var (name, _) in topics)
    {
        for (var i = 0; i < 10; i++)
        {
            await producer.ProduceAsync(name, new Message<string, string>
            {
                Key = $"key-{i}",
                Value = $"demo message {i} for {name}",
            });
        }
        Console.WriteLine($"  + {name}: 10 messages");
    }

    producer.Flush(TimeSpan.FromSeconds(10));
}

async Task CommitGroupOffsetsAsync()
{
    Console.WriteLine("\nCommitting consumer-group offsets ...");
    foreach (var (group, groupTopics) in groupOffsets)
    {
        // Commit offsets at the current end of each partition through a consumer in the group.
        // This registers the group in __consumer_offsets and gives it committed offsets, which
        // is what Kafdoc reads to derive group -> topic edges.
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(
            new ConsumerConfig(clientConfig) { GroupId = group, EnableAutoCommit = false }).Build();

        var offsets = new List<TopicPartitionOffset>();
        foreach (var topic in groupTopics)
        {
            var meta = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
            var topicMeta = meta.Topics.SingleOrDefault(t => t.Topic == topic);
            if (topicMeta is null)
            {
                continue;
            }

            foreach (var partition in topicMeta.Partitions)
            {
                var tp = new TopicPartition(topic, partition.PartitionId);
                var end = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(10)).High;
                offsets.Add(new TopicPartitionOffset(tp, end));
            }
        }

        if (offsets.Count == 0)
        {
            continue;
        }

        consumer.Commit(offsets);
        consumer.Close();
        Console.WriteLine($"  + {group} -> [{string.Join(", ", groupTopics)}]");
    }
}

static AclBinding TopicAcl(
    string user,
    string name,
    AclOperation op,
    ResourcePatternType pattern = ResourcePatternType.Literal) =>
    Acl(user, ResourceType.Topic, name, op, pattern);

static AclBinding GroupAcl(
    string user,
    string name,
    AclOperation op,
    ResourcePatternType pattern) =>
    Acl(user, ResourceType.Group, name, op, pattern);

static AclBinding Acl(
    string user,
    ResourceType resourceType,
    string name,
    AclOperation op,
    ResourcePatternType pattern) => new()
    {
        Pattern = new ResourcePattern
        {
            Type = resourceType,
            Name = name,
            ResourcePatternType = pattern,
        },
        Entry = new AccessControlEntry
        {
            Principal = $"User:{user}",
            Host = "*",
            Operation = op,
            PermissionType = AclPermissionType.Allow,
        },
    };
