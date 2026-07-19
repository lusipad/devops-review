using DevOpsReview.Bridge.Codex;

namespace DevOpsReview.Bridge.Tests.Codex;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task InitializesStartsThreadAndStreamsTurn()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "fixtures", "fake-codex-app-server.mjs");
        await using var client = new CodexAppServerClient("node", [script]);
        var events = new List<CodexStreamEvent>();

        var threadId = await client.StartThreadAsync(Environment.CurrentDirectory, CancellationToken.None);
        var result = await client.RunTurnAsync(
            threadId,
            "review this repository",
            (streamEvent, _) =>
            {
                events.Add(streamEvent);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("thread-fake", threadId);
        Assert.Equal("completed", result.Status);
        Assert.Contains(events, item =>
            item.Kind == CodexStreamEventKind.Delta &&
            item.Text == "来自伪 App Server 的回答");
        Assert.DoesNotContain(events, item => item.Text == "不应进入最终答案");

        await client.ResumeThreadAsync(threadId, Environment.CurrentDirectory, CancellationToken.None);
    }

    [Fact]
    public async Task CancellationInterruptsActiveTurn()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "fixtures", "fake-codex-app-server.mjs");
        await using var client = new CodexAppServerClient("node", [script]);
        var threadId = await client.StartThreadAsync(Environment.CurrentDirectory, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RunTurnAsync(
                threadId,
                "WAIT_FOR_INTERRUPT",
                (_, _) => Task.CompletedTask,
                cancellation.Token));
    }
}
