using ClaudeReviewBot.Core;

namespace ClaudeReviewBot.Tests.Fakes;

internal sealed class RecordingLog : IReviewLog
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warnings.Add(message);
}
