using DevOpsReview.Bridge.Configuration;

namespace DevOpsReview.Bridge.Tests.Configuration;

public sealed class BridgeOptionsValidatorTests
{
    [Fact]
    public void ValidateAcceptsExactExtensionAndRepositoryMapping()
    {
        var options = CreateOptions();

        BridgeOptionsValidator.Validate(options);
    }

    [Theory]
    [InlineData("chrome-extension://*/")]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("https://abcdefghijklmnopabcdefghijklmnop/")]
    public void ValidateRejectsNonExactExtensionOrigins(string origin)
    {
        var options = CreateOptions() with { AllowedExtensionOrigins = [origin] };

        Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsValidator.Validate(options));
    }

    [Fact]
    public void ValidateRejectsWorktreeRootInsideRepository()
    {
        var repository = Path.Combine(Path.GetTempPath(), "devops-review-source");
        var options = CreateOptions() with
        {
            WorktreeRoot = Path.Combine(repository, "worktrees"),
            Repositories = [CreateOptions().Repositories[0] with { LocalPath = repository }],
        };

        Assert.Throws<BridgeConfigurationException>(() => BridgeOptionsValidator.Validate(options));
    }

    private static BridgeOptions CreateOptions() => new()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "devops-review-data"),
        WorktreeRoot = Path.Combine(Path.GetTempPath(), "devops-review-worktrees"),
        AllowedExtensionOrigins = ["chrome-extension://abcdefghijklmnopabcdefghijklmnop/"],
        Repositories =
        [
            new RepositoryMapping
            {
                ServerUrl = "https://devops.example.test/tfs",
                Collection = "DefaultCollection",
                Project = "Orders",
                Repository = "Orders.Api",
                LocalPath = Path.Combine(Path.GetTempPath(), "devops-review-source"),
            },
        ],
    };
}
