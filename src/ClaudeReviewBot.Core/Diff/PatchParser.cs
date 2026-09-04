using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeReviewBot.Core.Diff;

/// <summary>
/// Everything the bot needs to know about a unified diff: which new-file line numbers were
/// added (the only lines GitHub accepts review comments on) and how to split a whole
/// <c>git diff</c> into per-file patches for offline runs.
/// </summary>
public static partial class PatchParser
{
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader { get; }

    /// <summary>
    /// Line numbers, in the new version of the file, of every line the patch adds.
    /// Claude reasons in file line numbers; GitHub only accepts positions that exist in the
    /// patch. This is the bridge between the two.
    /// </summary>
    public static IEnumerable<int> AddedLines(string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        int newLine = 0;
        bool inHunk = false;

        foreach (string raw in patch.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            Match header = HunkHeader.Match(line);
            if (header.Success)
            {
                newLine = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                inHunk = true;
                continue;
            }

            if (!inHunk || line.StartsWith('\\'))
            {
                continue; // preamble, or "\ No newline at end of file"
            }

            if (line.StartsWith('-'))
            {
                continue; // removed lines don't advance the new file
            }

            if (line.StartsWith('+'))
            {
                yield return newLine;
            }

            newLine++;
        }
    }

    /// <summary>The full set of (path, line) pairs a review comment may target.</summary>
    public static HashSet<(string Path, int Line)> CommentableLines(IEnumerable<ChangedFile> files)
    {
        HashSet<(string Path, int Line)> commentable = [];
        foreach (ChangedFile file in files)
        {
            foreach (int line in AddedLines(file.Patch))
            {
                commentable.Add((file.Path, line));
            }
        }

        return commentable;
    }

    /// <summary>
    /// Splits a complete unified diff (<c>git diff</c>, a <c>.patch</c> file) into the same
    /// per-file shape the GitHub API returns, so the offline path and the online path feed
    /// identical input to the reviewer. Deleted files are skipped: nothing can be commented on.
    /// </summary>
    public static IReadOnlyList<ChangedFile> ParseUnifiedDiff(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        List<ChangedFile> files = [];
        string? path = null;
        StringBuilder body = new();
        bool inHunk = false;

        void Flush()
        {
            if (path is not null && body.Length > 0)
            {
                files.Add(new ChangedFile(path, body.ToString().TrimEnd('\n')));
            }

            path = null;
            body.Clear();
            inHunk = false;
        }

        foreach (string raw in diff.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            if (!inHunk && line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                string target = line[4..];
                path = target == "/dev/null"
                    ? null
                    : target.StartsWith("b/", StringComparison.Ordinal) ? target[2..] : target;
                continue;
            }

            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                inHunk = true;
            }

            if (inHunk)
            {
                body.Append(line).Append('\n');
            }
        }

        Flush();
        return files;
    }
}
