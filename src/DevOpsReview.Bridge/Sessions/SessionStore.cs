using Microsoft.Data.Sqlite;

namespace DevOpsReview.Bridge.Sessions;

public sealed class SessionStore(string databasePath)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = false,
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS review_sessions (
                session_key TEXT PRIMARY KEY,
                repository_id TEXT NOT NULL,
                pull_request_id INTEGER NOT NULL,
                source_commit TEXT NOT NULL,
                target_commit TEXT NOT NULL,
                worktree_path TEXT NOT NULL,
                codex_thread_id TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewSession?> GetAsync(string sessionKey, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_key, repository_id, pull_request_id, source_commit, target_commit,
                   worktree_path, codex_thread_id, created_at, updated_at
            FROM review_sessions
            WHERE session_key = $session_key;
            """;
        command.Parameters.AddWithValue("$session_key", sessionKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ReviewSession(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    public async Task UpsertAsync(ReviewSession session, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO review_sessions (
                session_key, repository_id, pull_request_id, source_commit, target_commit,
                worktree_path, codex_thread_id, created_at, updated_at)
            VALUES (
                $session_key, $repository_id, $pull_request_id, $source_commit, $target_commit,
                $worktree_path, $codex_thread_id, $created_at, $updated_at)
            ON CONFLICT(session_key) DO UPDATE SET
                target_commit = excluded.target_commit,
                worktree_path = excluded.worktree_path,
                codex_thread_id = excluded.codex_thread_id,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$session_key", session.SessionKey);
        command.Parameters.AddWithValue("$repository_id", session.RepositoryId);
        command.Parameters.AddWithValue("$pull_request_id", session.PullRequestId);
        command.Parameters.AddWithValue("$source_commit", session.SourceCommit);
        command.Parameters.AddWithValue("$target_commit", session.TargetCommit);
        command.Parameters.AddWithValue("$worktree_path", session.WorktreePath);
        command.Parameters.AddWithValue("$codex_thread_id", (object?)session.CodexThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", session.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", session.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}

public sealed record ReviewSession(
    string SessionKey,
    string RepositoryId,
    int PullRequestId,
    string SourceCommit,
    string TargetCommit,
    string WorktreePath,
    string? CodexThreadId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static string CreateKey(string repositoryId, int pullRequestId, string sourceCommit) =>
        $"{repositoryId}:{pullRequestId}:{sourceCommit}";
}
