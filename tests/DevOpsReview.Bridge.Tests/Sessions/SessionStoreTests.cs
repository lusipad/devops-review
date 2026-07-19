using DevOpsReview.Bridge.Sessions;

namespace DevOpsReview.Bridge.Tests.Sessions;

public sealed class SessionStoreTests
{
    [Fact]
    public async Task PersistsAndUpdatesThreadIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"devops-review-session-{Guid.NewGuid():N}");
        try
        {
            var store = new SessionStore(Path.Combine(directory, "sessions.db"));
            await store.InitializeAsync(CancellationToken.None);
            var created = DateTimeOffset.UtcNow;
            var session = new ReviewSession(
                "repo:42:aaaaaaaa",
                "repo",
                42,
                new string('a', 40),
                new string('b', 40),
                @"D:\Worktrees\repo\42",
                null,
                created,
                created);

            await store.UpsertAsync(session, CancellationToken.None);
            await store.UpsertAsync(
                session with { CodexThreadId = "thread-123", UpdatedAt = created.AddMinutes(1) },
                CancellationToken.None);
            var restored = await store.GetAsync(session.SessionKey, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal("thread-123", restored.CodexThreadId);
            Assert.Equal(session.SourceCommit, restored.SourceCommit);
            Assert.Equal(created.AddMinutes(1), restored.UpdatedAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
