using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Git;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.Tests.Git;

public sealed class WorktreeManagerTests
{
    [Fact]
    public async Task CreatesDetachedWorktreeWithoutSwitchingSourceRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devops-review-worktree-{Guid.NewGuid():N}");
        var repositoryPath = Path.Combine(root, "source");
        var worktreeRoot = Path.Combine(root, "reviews");
        Directory.CreateDirectory(repositoryPath);
        try
        {
            var sourceCommit = new string('a', 40);
            var runner = new RecordingGitRunner(repositoryPath, sourceCommit);
            var options = new BridgeOptions
            {
                DataDirectory = Path.Combine(root, "data"),
                WorktreeRoot = worktreeRoot,
                AllowedExtensionOrigins = ["chrome-extension://abcdefghijklmnopabcdefghijklmnop/"],
                Repositories = [],
            };
            var mapping = new RepositoryMapping
            {
                ServerUrl = "https://devops.example.test/tfs",
                Collection = "DefaultCollection",
                Project = "Orders",
                Repository = "Orders.Api",
                LocalPath = repositoryPath,
            };
            var request = new ValidatedReviewRequest(
                new Uri(mapping.ServerUrl), mapping.Collection, mapping.Project, mapping.Repository,
                42, "/src/Order.cs", 1, 2, "selected", "question", mapping);
            var pullRequest = new PullRequestContext(
                "repo-guid", 42, "refs/heads/feature", "refs/heads/main",
                sourceCommit, new string('b', 40), 1);

            var result = await new WorktreeManager(options, runner)
                .PrepareAsync(request, pullRequest, CancellationToken.None);

            Assert.True(File.Exists(result.SelectedFilePath));
            Assert.Contains(runner.Commands, command => command.SequenceEqual(["fetch", "--prune", "origin"]));
            Assert.Contains(runner.Commands, command => command.Count >= 3 && command[0] == "worktree" && command[2] == "--detach");
            Assert.DoesNotContain(runner.Commands, command => command.Contains("switch"));
            Assert.DoesNotContain(runner.Commands, command => command.Contains("checkout"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RecordingGitRunner(string repositoryPath, string sourceCommit) : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string currentRepositoryPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Commands.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["rev-parse", "--show-toplevel"]))
            {
                return Task.FromResult(new GitCommandResult(repositoryPath, string.Empty));
            }

            if (arguments.Count >= 5 && arguments[0] == "worktree" && arguments[1] == "add")
            {
                var path = arguments[3];
                Directory.CreateDirectory(Path.Combine(path, "src"));
                File.WriteAllText(Path.Combine(path, "src", "Order.cs"), "line one\nline two\n");
            }

            if (arguments.SequenceEqual(["rev-parse", "HEAD"]))
            {
                return Task.FromResult(new GitCommandResult(sourceCommit, string.Empty));
            }

            return Task.FromResult(new GitCommandResult(string.Empty, string.Empty));
        }
    }
}
