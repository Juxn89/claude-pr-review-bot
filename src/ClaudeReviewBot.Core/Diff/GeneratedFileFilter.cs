using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeReviewBot.Core.Diff;

/// <summary>
/// Files nobody reviews by hand and a model should not review either: lockfiles, designer
/// output, minified bundles, generated migrations. Skipping them is the single biggest lever
/// on both cost and noise: the original 47-comment incident was 41 comments on a lockfile.
/// </summary>
public sealed class GeneratedFileFilter
{
    public static readonly IReadOnlyList<string> DefaultPatterns =
    [
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "bun.lockb",
        "packages.lock.json",
        "Cargo.lock",
        "poetry.lock",
        "Pipfile.lock",
        "Gemfile.lock",
        "composer.lock",
        "go.sum",
        "*.Designer.cs",
        "*.g.cs",
        "*.g.i.cs",
        "*.generated.cs",
        "*.min.js",
        "*.min.css",
        "*.map",
        "*.snap",
        "**/Migrations/*.cs",
        "**/*ModelSnapshot.cs",
    ];

    private readonly Regex[] _matchers;

    public GeneratedFileFilter(IEnumerable<string>? extraPatterns = null)
    {
        Patterns = [.. DefaultPatterns, .. (extraPatterns ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())];
        _matchers = [.. Patterns.Select(GlobToRegex)];
    }

    public IReadOnlyList<string> Patterns { get; }

    public bool IsGenerated(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalized = path.Replace('\\', '/');
        return _matchers.Any(m => m.IsMatch(normalized));
    }

    /// <summary>
    /// Minimal glob to regex: <c>*</c> matches within a segment, <c>**/</c> matches any number of
    /// segments, a pattern without a slash matches the file name anywhere in the tree.
    /// </summary>
    internal static Regex GlobToRegex(string glob)
    {
        StringBuilder pattern = new("^");
        if (!glob.Contains('/'))
        {
            pattern.Append("(?:.*/)?");
        }

        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                bool consumesSlash = i + 2 < glob.Length && glob[i + 2] == '/';
                pattern.Append(consumesSlash ? "(?:.*/)?" : ".*");
                i += consumesSlash ? 2 : 1;
            }
            else if (c == '*')
            {
                pattern.Append("[^/]*");
            }
            else if (c == '?')
            {
                pattern.Append("[^/]");
            }
            else
            {
                pattern.Append(Regex.Escape(c.ToString()));
            }
        }

        pattern.Append('$');
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
