using DevOpsReview.Bridge.Git;

namespace DevOpsReview.Bridge.Tests.Git;

public sealed class GitProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncCompletesWhenGitWritesRedirectedOutput()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"devops-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var runner = new GitProcessRunner();
            await runner.RunAsync(
                repositoryPath,
                ["init"],
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            var result = await runner.RunAsync(
                repositoryPath,
                ["rev-parse", "--show-toplevel"],
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            Assert.Equal(Path.GetFullPath(repositoryPath), Path.GetFullPath(result.StandardOutput));
        }
        finally
        {
            Directory.Delete(repositoryPath, recursive: true);
        }
    }
}
