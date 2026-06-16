namespace Kafdoc.Domain.Documentation;

/// <summary>Pure, dependency-free glob matching for documentation aliases.</summary>
public static class DocumentationPattern
{
    /// <summary>
    /// Ordinal, case-sensitive <c>*</c> glob match where <c>*</c> matches any run of
    /// characters (including dots). A pattern containing no <c>*</c> is an exact match.
    /// </summary>
    /// <param name="pattern">The glob or exact pattern.</param>
    /// <param name="name">The raw name to test.</param>
    /// <returns><c>true</c> when <paramref name="name"/> matches <paramref name="pattern"/>.</returns>
    public static bool Matches(string pattern, string name)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(name);

        var p = 0;
        var n = 0;
        var star = -1;
        var mark = 0;

        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p;
                mark = n;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == name[n])
            {
                p++;
                n++;
            }
            else if (star != -1)
            {
                p = star + 1;
                mark++;
                n = mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
