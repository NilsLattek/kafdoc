namespace Kafdoc.Domain.Kafka;

/// <summary>Whether an ACL grants or denies access.</summary>
public enum KafkaAclPermission
{
    /// <summary>Permission type not recognised.</summary>
    Other = 0,
    /// <summary>Access is allowed.</summary>
    Allow,
    /// <summary>Access is denied.</summary>
    Deny,
}
