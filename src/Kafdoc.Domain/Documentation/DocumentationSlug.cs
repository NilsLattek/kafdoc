namespace Kafdoc.Domain.Documentation;

/// <summary>Maps topic names and principals to documentation file slugs (no folder, no extension).</summary>
public static class DocumentationSlug
{
    private static readonly char[] IllegalChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>The slug for a topic.</summary>
    /// <param name="topicName">The topic name.</param>
    /// <returns>The file slug.</returns>
    public static string ForTopic(string topicName) => Slug(topicName);

    /// <summary>The slug for a principal; the <c>User:</c> prefix is stripped first.</summary>
    /// <param name="principal">The principal, e.g. <c>User:svc-payments</c>.</param>
    /// <returns>The file slug.</returns>
    public static string ForUser(string principal)
    {
        const string prefix = "User:";
        var name = principal.StartsWith(prefix, StringComparison.Ordinal)
            ? principal[prefix.Length..]
            : principal;
        return Slug(name);
    }

    private static string Slug(string value)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            chars[i] = Array.IndexOf(IllegalChars, value[i]) >= 0 ? '_' : value[i];
        }

        return new string(chars).TrimStart('.');
    }
}
