namespace Kafdoc.Domain.Graph;

/// <summary>A user (principal) node in the cluster graph.</summary>
/// <param name="Principal">The principal, e.g. <c>User:svc-payments</c>.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials on the cluster.</param>
public sealed record KafkaUser(string Principal, bool HasScramCredentials);
