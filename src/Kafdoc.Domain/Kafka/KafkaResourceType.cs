namespace Kafdoc.Domain.Kafka;

/// <summary>The type of resource an ACL applies to.</summary>
public enum KafkaResourceType
{
    /// <summary>A resource type not separately modelled.</summary>
    Other = 0,
    /// <summary>A topic resource.</summary>
    Topic,
    /// <summary>A consumer group resource.</summary>
    Group,
    /// <summary>The cluster resource.</summary>
    Cluster,
    /// <summary>A transactional id resource.</summary>
    TransactionalId,
}
