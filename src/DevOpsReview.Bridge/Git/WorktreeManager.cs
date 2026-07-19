using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.Git;

public sealed class WorktreeManager(BridgeOptions options, IGitProcessRunner git)
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    public async Task<PreparedWorktree> PrepareAsync(
        ValidatedReviewRequest request,
        PullRequestContext pullRequest,
        CancellationToken cancellationToken)
    {
        var repositoryPath = Path.GetFullPath(request.Mapping.LocalPath);
        if (!Directory.Exists(repositoryPath))
        {
            throw new WorktreeException("local_repository_missing", "Configured local repository does not exist.");
        }

        await VerifyRepositoryAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        await git.RunAsync(
            repositoryPath,
            ["fetch", "--prune", "origin"],
            GitTimeout,
            cancellationToken).ConfigureAwait(false);
        await git.RunAsync(
            repositoryPath,
            ["cat-file", "-e", $"{pullRequest.SourceCommit}^{{commit}}"],
            GitTimeout,
            cancellationToken).ConfigureAwait(false);
        await git.RunAsync(
            repositoryPath,
            ["cat-file", "-e", $"{pullRequest.TargetCommit}^{{commit}}"],
            GitTimeout,
            cancellationToken).ConfigureAwait(false);

        var worktreePath = BuildWorktreePath(pullRequest);
        if (Directory.Exists(worktreePath))
        {
            var existingHead = await git.RunAsync(
                worktreePath,
                ["rev-parse", "HEAD"],
                GitTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(existingHead.StandardOutput, pullRequest.SourceCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorktreeException(
                    "worktree_commit_mismatch",
                    "Existing PR worktree is not at the expected source commit.");
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
            await git.RunAsync(
                repositoryPath,
                ["worktree", "add", "--detach", worktreePath, pullRequest.SourceCommit],
                GitTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        var selectedFile = ResolveSelectedFile(worktreePath, request.FilePath);
        if (!File.Exists(selectedFile))
        {
            throw new WorktreeException(
                "selected_file_missing",
                "Selected file does not exist at the current PR source commit.");
        }

        return new PreparedWorktree(worktreePath, selectedFile);
    }

    private async Task VerifyRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(
            repositoryPath,
            ["rev-parse", "--show-toplevel"],
            GitTimeout,
            cancellationToken).ConfigureAwait(false);
        var configured = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.StandardOutput));
        if (!string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorktreeException(
                "invalid_local_repository",
                "Configured local path must be the Git repository root.");
        }
    }

    private string BuildWorktreePath(PullRequestContext pullRequest)
    {
        var root = Path.GetFullPath(options.WorktreeRoot);
        var repository = SafePathSegment(pullRequest.RepositoryId);
        var path = Path.GetFullPath(Path.Combine(
            root,
            repository,
            $"pr-{pullRequest.PullRequestId}",
            pullRequest.SourceCommit));
        EnsureDescendant(root, path);
        return path;
    }

    private static string ResolveSelectedFile(string worktreePath, string repositoryFilePath)
    {
        var relativePath = repositoryFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var selectedFile = Path.GetFullPath(Path.Combine(worktreePath, relativePath));
        EnsureDescendant(worktreePath, selectedFile);
        return selectedFile;
    }

    private static void EnsureDescendant(string root, string path)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorktreeException("path_escape", "Resolved path is outside the configured worktree root.");
        }
    }

    private static string SafePathSegment(string value)
    {
        var filtered = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        if (filtered.Length == 0)
        {
            throw new WorktreeException("invalid_repository_id", "Azure DevOps repository ID is invalid.");
        }

        return filtered;
    }
}

public sealed record PreparedWorktree(string Path, string SelectedFilePath);

public sealed class WorktreeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
