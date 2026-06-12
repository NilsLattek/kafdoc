using Kafdoc.Domain.Entities;
using Kafdoc.Domain.Common;

namespace Kafdoc.Application.Dtos;

/// <summary>
/// Data transfer object for adding a child node to an org chart.
/// </summary>
public class AddTopicDto
{
    public string Name { get; }
}
