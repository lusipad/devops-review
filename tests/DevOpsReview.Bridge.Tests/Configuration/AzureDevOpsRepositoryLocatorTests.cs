using DevOpsReview.Bridge.Configuration;

namespace DevOpsReview.Bridge.Tests.Configuration;

public sealed class AzureDevOpsRepositoryLocatorTests
{
    [Fact]
    public void ParsesAzureDevOpsServerPullRequestUrl()
    {
        var result = AzureDevOpsRepositoryLocator.Parse(
            "http://host:8080/tfs/DefaultCollection/Orders/_git/Orders.Api/pullrequest/42");

        Assert.Equal("http://host:8080/tfs", result.ServerUrl);
        Assert.Equal("DefaultCollection", result.Collection);
        Assert.Equal("Orders", result.Project);
        Assert.Equal("Orders.Api", result.Repository);
    }

    [Fact]
    public void DecodesRepositoryLocatorValues()
    {
        var result = AzureDevOpsRepositoryLocator.Parse(
            "https://dev.azure.com/contoso/Orders%20Project/_git/Orders%20Api");

        Assert.Equal("https://dev.azure.com", result.ServerUrl);
        Assert.Equal("contoso", result.Collection);
        Assert.Equal("Orders Project", result.Project);
        Assert.Equal("Orders Api", result.Repository);
    }

    [Fact]
    public void RepositoryIdentityIncludesServerCollectionProjectAndRepository()
    {
        var expected = AzureDevOpsRepositoryLocator.Parse(
            "https://devops.example/tfs/DefaultCollection/Orders/_git/Orders.Api");
        var same = AzureDevOpsRepositoryLocator.Parse(
            "https://DEVOPS.example/tfs/defaultcollection/orders/_git/orders.api/pullrequest/42");
        var differentProject = AzureDevOpsRepositoryLocator.Parse(
            "https://devops.example/tfs/DefaultCollection/Payments/_git/Orders.Api");

        Assert.True(expected.IdentifiesSameRepository(same));
        Assert.False(expected.IdentifiesSameRepository(differentProject));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://host/tfs/DefaultCollection/Project")]
    public void RejectsUnsupportedUrls(string value)
    {
        Assert.Throws<FormatException>(() => AzureDevOpsRepositoryLocator.Parse(value));
    }
}
