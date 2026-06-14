namespace Kafdoc.Application.Snapshot;

/// <summary>Options controlling how often the cluster snapshot is refreshed.</summary>
public sealed class RefreshOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka";

    /// <summary>How often to refresh the snapshot from the cluster. Defaults to one hour.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);
}
