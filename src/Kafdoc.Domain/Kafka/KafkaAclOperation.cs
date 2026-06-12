namespace Kafdoc.Domain.Kafka;

/// <summary>Kafka ACL operations relevant to the producer/consumer graph.</summary>
public enum KafkaAclOperation
{
    /// <summary>Any operation not separately modelled.</summary>
    Other = 0,
    /// <summary>Permission to read (consume) from a resource.</summary>
    Read,
    /// <summary>Permission to write (produce) to a resource.</summary>
    Write,
    /// <summary>Permission covering all operations.</summary>
    All,
}
