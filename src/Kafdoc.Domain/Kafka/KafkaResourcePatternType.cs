namespace Kafdoc.Domain.Kafka;

/// <summary>How an ACL resource name should be matched.</summary>
public enum KafkaResourcePatternType
{
    /// <summary>A pattern type not separately modelled.</summary>
    Other = 0,
    /// <summary>The name matches a single resource exactly.</summary>
    Literal,
    /// <summary>The name is a prefix matching any resource starting with it.</summary>
    Prefixed,
}
