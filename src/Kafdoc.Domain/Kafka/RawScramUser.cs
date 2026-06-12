namespace Kafdoc.Domain.Kafka;

/// <summary>A user with SCRAM credentials declared on the cluster.</summary>
/// <param name="Principal">The principal, e.g. <c>User:svc-payments</c>.</param>
public sealed record RawScramUser(string Principal);
