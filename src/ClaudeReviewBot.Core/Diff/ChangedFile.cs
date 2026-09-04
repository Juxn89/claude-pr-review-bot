namespace ClaudeReviewBot.Core.Diff;

/// <summary>One file touched by the pull request, with its unified-diff patch exactly as GitHub returns it.</summary>
public sealed record ChangedFile(string Path, string Patch);
