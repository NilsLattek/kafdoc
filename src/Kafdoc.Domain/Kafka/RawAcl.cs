namespace Kafdoc.Domain.Kafka;

/// <summary>A single ACL binding as read from the cluster.</summary>
/// <param name="Principal">The principal the ACL applies to, e.g. <c>User:svc-payments</c>.</param>
/// <param name="ResourceType">The resource type the ACL targets.</param>
/// <param name="ResourceName">The resource name or prefix.</param>
/// <param name="PatternType">How <paramref name="ResourceName"/> is matched.</param>
/// <param name="Operation">The operation granted or denied.</param>
/// <param name="Permission">Whether the operation is allowed or denied.</param>
public sealed record RawAcl(
    string Principal,
    KafkaResourceType ResourceType,
    string ResourceName,
    KafkaResourcePatternType PatternType,
    KafkaAclOperation Operation,
    KafkaAclPermission Permission);
