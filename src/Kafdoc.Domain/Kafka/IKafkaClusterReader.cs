namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Reads the raw facts (topics, ACLs, consumer groups, users) from a Kafka cluster.
/// Implementations live in the Infrastructure layer.
/// </summary>
public interface IKafkaClusterReader
{
    /// <summary>
    /// Reads the current topics, ACLs, consumer groups, and SCRAM users from the cluster.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The raw cluster data.</returns>
    Task<RawClusterData> ReadAsync(CancellationToken cancellationToken);
}
