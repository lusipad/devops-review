using DevOpsReview.Bridge.Orchestration;

namespace DevOpsReview.Bridge.Tests.Orchestration;

public sealed class ReviewOutputSanitizerTests
{
    [Fact]
    public void RemovesWorktreePathsAndLocalFileLinks()
    {
        const string root = @"C:\Users\dev\AppData\Local\DevOpsReview\worktrees\repo\pr-4\sha";
        const string answer = "See [src/tax.js:5](C:/Users/dev/AppData/Local/DevOpsReview/worktrees/repo/pr-4/sha/src/tax.js:5) and C:\\Users\\dev\\AppData\\Local\\DevOpsReview\\worktrees\\repo\\pr-4\\sha\\src\\checkout.js:8.";

        var sanitized = ReviewOutputSanitizer.Sanitize(answer, root);

        Assert.Equal("See src/tax.js:5 and src\\checkout.js:8.", sanitized);
        Assert.DoesNotContain("C:", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
