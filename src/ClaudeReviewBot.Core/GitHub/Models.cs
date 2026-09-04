namespace ClaudeReviewBot.Core.GitHub;

public sealed record PullRequestInfo(int Number, string Title, string HeadSha, string BaseSha);

/// <summary>A single inline comment. <c>Line</c> is a new-file line number on the RIGHT side.</summary>
public sealed record ReviewComment(string Path, int Line, string Body);

/// <summary>
/// One review, submitted atomically. Either the whole thing lands or none of it does; the
/// half-posted review that a per-comment tool call produces on a timeout cannot happen here.
/// </summary>
public sealed record ReviewSubmission(string CommitId, string Body, IReadOnlyList<ReviewComment> Comments);
