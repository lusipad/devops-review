using System.Net;
using System.Text;
using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.Tests.AzureDevOps;

public sealed class AzureDevOpsClientTests
{
    [Fact]
    public async Task TestsConfiguredRepositoryWithWindowsAuthenticationPath()
    {
        Uri? requestedUri = null;
        using var handler = new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse("{\"id\":\"repo-guid\"}");
        });
        var client = new AzureDevOpsClient(_ => handler);
        var mapping = CreateRequest().Mapping;

        await client.TestConnectionAsync(mapping, CancellationToken.None);

        Assert.NotNull(requestedUri);
        Assert.Equal(
            "/tfs/DefaultCollection/Orders%20Project/_apis/git/repositories/Orders.Api",
            requestedUri.AbsolutePath);
        Assert.Equal("7.0", GetQueryValue(requestedUri, "api-version"));
    }

    [Fact]
    public async Task GetsLatestIterationCommitsFromConfiguredServer()
    {
        var requests = new List<Uri>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            var json = request.RequestUri!.AbsolutePath.EndsWith("/iterations", StringComparison.Ordinal)
                ? """
                  {"value":[
                    {"id":1,"sourceRefCommit":{"commitId":"1111111111111111111111111111111111111111"},"targetRefCommit":{"commitId":"2222222222222222222222222222222222222222"}},
                    {"id":2,"sourceRefCommit":{"commitId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"targetRefCommit":{"commitId":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}
                  ]}
                  """
                : """
                  {
                    "repository":{"id":"repo-guid"},
                    "sourceRefName":"refs/heads/feature/review",
                    "targetRefName":"refs/heads/main",
                    "lastMergeSourceCommit":{"commitId":"3333333333333333333333333333333333333333"},
                    "lastMergeTargetCommit":{"commitId":"4444444444444444444444444444444444444444"}
                  }
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        var client = new AzureDevOpsClient(_ => handler);

        var result = await client.GetPullRequestContextAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("repo-guid", result.RepositoryId);
        Assert.Equal(2, result.IterationId);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.SourceCommit);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.TargetCommit);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, uri => Assert.Equal("7.0", GetQueryValue(uri, "api-version")));
        Assert.Contains(
            "/DefaultCollection/Orders%20Project/_apis/git/repositories/Orders.Api/pullrequests/1427",
            requests[0].AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishesServerHeldAnswerAsInlineCommentWhenChangeIsKnown()
    {
        string? postedJson = null;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""
                    {"changeEntries":[{"changeTrackingId":7,"item":{"path":"/src/Order.cs"}}]}
                    """);
            }

            postedJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("{\"id\":99}");
        });
        var client = new AzureDevOpsClient(_ => handler);
        var pullRequest = new PullRequestContext(
            "repo-guid",
            1427,
            "refs/heads/feature/review",
            "refs/heads/main",
            new string('a', 40),
            new string('b', 40),
            2);

        var result = await client.PublishReviewAsync(
            CreateRequest(),
            pullRequest,
            "存在竞态条件，证据见 `src/Order.cs:10`。",
            CancellationToken.None);

        Assert.Equal(99, result.ThreadId);
        Assert.True(result.IsInline);
        Assert.NotNull(postedJson);
        using var document = System.Text.Json.JsonDocument.Parse(postedJson);
        Assert.Equal(
            7,
            document.RootElement
                .GetProperty("pullRequestThreadContext")
                .GetProperty("changeTrackingId")
                .GetInt32());
        Assert.Equal(
            10,
            document.RootElement
                .GetProperty("threadContext")
                .GetProperty("rightFileStart")
                .GetProperty("line")
                .GetInt32());
        Assert.Contains(
            "存在竞态条件",
            document.RootElement.GetProperty("comments")[0].GetProperty("content").GetString(),
            StringComparison.Ordinal);
    }

    private static ValidatedReviewRequest CreateRequest()
    {
        var mapping = new RepositoryMapping
        {
            ServerUrl = "https://devops.example.test/tfs",
            Collection = "DefaultCollection",
            Project = "Orders Project",
            Repository = "Orders.Api",
            LocalPath = @"D:\Source\Orders.Api",
        };
        return new ValidatedReviewRequest(
            new Uri(mapping.ServerUrl),
            mapping.Collection,
            mapping.Project,
            mapping.Repository,
            1427,
            "/src/Order.cs",
            10,
            20,
            "selected",
            "question",
            mapping);
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == key)
            {
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            }
        }

        return null;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
