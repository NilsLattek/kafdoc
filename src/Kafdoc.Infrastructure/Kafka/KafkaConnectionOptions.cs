namespace Kafdoc.Infrastructure.Kafka;

/// <summary>Connection settings for the Kafka Admin client.</summary>
public sealed class KafkaConnectionOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated bootstrap servers, e.g. <c>broker:9092</c>.</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Security protocol, e.g. <c>SaslSsl</c> or <c>SaslPlaintext</c>.</summary>
    public string SecurityProtocol { get; set; } = "SaslSsl";

    /// <summary>SASL mechanism, e.g. <c>ScramSha512</c>.</summary>
    public string SaslMechanism { get; set; } = "ScramSha512";

    /// <summary>SASL username.</summary>
    public string SaslUsername { get; set; } = string.Empty;

    /// <summary>SASL password.</summary>
    public string SaslPassword { get; set; } = string.Empty;

    /// <summary>Optional path to a CA certificate bundle for TLS.</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>Admin request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
