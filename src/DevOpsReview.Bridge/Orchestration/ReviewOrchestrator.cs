using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Codex;
using DevOpsReview.Bridge.Git;
using DevOpsReview.Bridge.Security;
using DevOpsReview.Bridge.Sessions;

namespace DevOpsReview.Bridge.Orchestration;

public sealed class ReviewOrchestrator(
    AzureDevOpsClient azureDevOps,
    WorktreeManager worktrees,
    SessionStore sessions,
    CodexAppServerClient codex)
{
    public async Task<ReviewExecutionResult> RunAsync(
        ValidatedReviewRequest request,
        Func<ReviewExecutionEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        await onEvent(
            new ReviewExecutionEvent(ReviewExecutionEventKind.Progress, "正在校验 Azure DevOps PR 版本。"),
            cancellationToken).ConfigureAwait(false);
        var pullRequest = await azureDevOps.GetPullRequestContextAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await onEvent(
            new ReviewExecutionEvent(ReviewExecutionEventKind.Progress, "正在准备 PR 专用只读工作树。"),
            cancellationToken).ConfigureAwait(false);
        var worktree = await worktrees.PrepareAsync(request, pullRequest, cancellationToken)
            .ConfigureAwait(false);

        var sessionKey = ReviewSession.CreateKey(
            pullRequest.RepositoryId,
            pullRequest.PullRequestId,
            pullRequest.SourceCommit);
        var session = await sessions.GetAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        session ??= new ReviewSession(
            sessionKey,
            pullRequest.RepositoryId,
            pullRequest.PullRequestId,
            pullRequest.SourceCommit,
            pullRequest.TargetCommit,
            worktree.Path,
            null,
            now,
            now);

        var threadId = await GetOrCreateThreadAsync(session, worktree.Path, cancellationToken)
            .ConfigureAwait(false);
        session = session with
        {
            WorktreePath = worktree.Path,
            TargetCommit = pullRequest.TargetCommit,
            CodexThreadId = threadId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await sessions.UpsertAsync(session, cancellationToken).ConfigureAwait(false);

        var prompt = ReviewPromptBuilder.Build(request, pullRequest, worktree);
        var turn = await codex.RunTurnAsync(
            threadId,
            prompt,
            async (codexEvent, eventCancellationToken) =>
            {
                var kind = codexEvent.Kind == CodexStreamEventKind.Delta
                    ? ReviewExecutionEventKind.Delta
                    : ReviewExecutionEventKind.Progress;
                await onEvent(
                    new ReviewExecutionEvent(kind, codexEvent.Text),
                    eventCancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new ReviewExecutionResult(
            sessionKey,
            threadId,
            turn.TurnId,
            pullRequest.SourceCommit,
            pullRequest.TargetCommit,
            worktree.Path,
            pullRequest);
    }

    public Task<PublishedComment> PublishAsync(
        ValidatedReviewRequest request,
        ReviewExecutionResult result,
        string answer,
        CancellationToken cancellationToken) =>
        azureDevOps.PublishReviewAsync(request, result.PullRequest, answer, cancellationToken);

    private async Task<string> GetOrCreateThreadAsync(
        ReviewSession session,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(session.CodexThreadId))
        {
            try
            {
                await codex.ResumeThreadAsync(session.CodexThreadId, worktreePath, cancellationToken)
                    .ConfigureAwait(false);
                return session.CodexThreadId;
            }
            catch (CodexAppServerException exception) when (exception.Code == "codex_request_failed")
            {
                Console.Error.WriteLine("Stored Codex thread could not be resumed; creating a replacement.");
            }
        }

        return await codex.StartThreadAsync(worktreePath, cancellationToken).ConfigureAwait(false);
    }
}

public enum ReviewExecutionEventKind
{
    Progress,
    Delta,
}

public sealed record ReviewExecutionEvent(ReviewExecutionEventKind Kind, string Text);

public sealed record ReviewExecutionResult(
    string SessionKey,
    string ThreadId,
    string TurnId,
    string SourceCommit,
    string TargetCommit,
    string WorktreePath,
    PullRequestContext PullRequest);
