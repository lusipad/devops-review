using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Protocol;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.Tests.Security;

public sealed class ReviewRequestValidatorTests
{
    private static readonly BridgeOptions Options = new()
    {
        Repositories =
        [
            new RepositoryMapping
            {
                ServerUrl = "https://devops.example.test/tfs/",
                Collection = "DefaultCollection",
                Project = "Orders",
                Repository = "Orders.Api",
                LocalPath = @"D:\Source\Orders.Api",
            },
        ],
    };

    [Fact]
    public void ValidateNormalizesConfiguredRequest()
    {
        var request = CreateRequest(filePath: "src/Orders/OrderService.cs");

        var result = new ReviewRequestValidator(Options).Validate(request);

        Assert.Equal("https://devops.example.test/tfs", result.ServerUri.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("/src/Orders/OrderService.cs", result.FilePath);
        Assert.Equal(@"D:\Source\Orders.Api", result.Mapping.LocalPath);
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("src/../secrets.txt")]
    [InlineData(@"C:\secrets.txt")]
    [InlineData(@"src\Orders\OrderService.cs")]
    public void ValidateRejectsUnsafeFilePaths(string filePath)
    {
        var exception = Assert.Throws<ReviewRequestValidationException>(() =>
            new ReviewRequestValidator(Options).Validate(CreateRequest(filePath)));

        Assert.Equal("invalid_file_path", exception.Code);
    }

    [Fact]
    public void ValidateRejectsUnconfiguredRepository()
    {
        var request = CreateRequest() with { Repository = "Other" };

        var exception = Assert.Throws<ReviewRequestValidationException>(() =>
            new ReviewRequestValidator(Options).Validate(request));

        Assert.Equal("repository_not_allowed", exception.Code);
    }

    [Fact]
    public void ValidateRejectsExcessiveLineRange()
    {
        var request = CreateRequest() with { StartLine = 1, EndLine = 1_001 };

        var exception = Assert.Throws<ReviewRequestValidationException>(() =>
            new ReviewRequestValidator(Options).Validate(request));

        Assert.Equal("invalid_line_range", exception.Code);
    }

    private static ReviewStartRequest CreateRequest(string filePath = "/src/Orders/OrderService.cs") => new(
        "https://devops.example.test/tfs",
        "DefaultCollection",
        "Orders",
        "Orders.Api",
        1427,
        filePath,
        125,
        141,
        "await repository.InsertAsync(order);",
        "这里并发时会不会重复创建？");
}
