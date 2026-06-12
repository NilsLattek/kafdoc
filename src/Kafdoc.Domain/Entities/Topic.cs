namespace Kafdoc.Domain.Entities;

public sealed class Topic
{
    public string Name { get; init; }
    internal Topic(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Chart name is required.", nameof(name));
        }

        Name = name.Trim();
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private Topic() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}
