namespace ClaudeReviewBot.Core;

/// <summary>
/// Minimal logging seam. The bot runs as a short-lived CI job, so a full logging
/// framework buys nothing; what matters is that warnings surface in the Actions UI.
/// </summary>
public interface IReviewLog
{
    void Info(string message);

    void Warn(string message);
}

/// <summary>Writes to a <see cref="TextWriter"/>; emits GitHub Actions annotations when running there.</summary>
public sealed class ConsoleReviewLog(TextWriter output, bool gitHubAnnotations) : IReviewLog
{
    public static ConsoleReviewLog ForEnvironment() =>
        new(Console.Out, string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase));

    public void Info(string message) => output.WriteLine(message);

    public void Warn(string message) => output.WriteLine(gitHubAnnotations ? $"::warning::{message}" : $"warning: {message}");
}

public sealed class NullReviewLog : IReviewLog
{
    public static NullReviewLog Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }
}
