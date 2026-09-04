namespace ClaudeReviewBot.Core.GitHub;

/// <summary>
/// The model chooses the paths <c>read_file</c> fetches. Reading contributor code as data is
/// safe; letting a path escape the repository is not, even through an API that would probably
/// reject it anyway.
/// </summary>
public static class RepoPath
{
    public const int MaxLength = 4096;

    public static bool IsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength)
        {
            return false;
        }

        if (path.Contains('\\') || path.Contains('\0') || path.StartsWith('/') || path.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        return path.Split('/').All(segment => segment is not ("" or "." or ".."));
    }
}
