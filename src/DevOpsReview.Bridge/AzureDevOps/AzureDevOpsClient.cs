using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Protocol;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.AzureDevOps;

public sealed class AzureDevOpsClient
{
    private const int MaxPublishedCommentLength = 60_000;
    private readonly Func<RepositoryMapping, HttpMessageHandler> handlerFactory;

    public AzureDevOpsClient(Func<RepositoryMapping, HttpMessageHandler>? handlerFactory = null)
    {
        this.handlerFactory = handlerFactory ?? (mapping => CreateHandler(mapping));
    }

    public async Task TestConnectionAsync(
        RepositoryMapping mapping,
        CancellationToken cancellationToken)
    {
        using var handler = handlerFactory(mapping);
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        ApplyAuthentication(client, mapping);

        var server = mapping.ServerUrl.TrimEnd('/');
        using var response = await GetJsonAsync(
            client,
            $"{server}/{Escape(mapping.Collection)}/{Escape(mapping.Project)}/_apis/git/repositories/{Escape(mapping.Repository)}?api-version={Escape(mapping.ApiVersion)}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PullRequestContext> GetPullRequestContextAsync(
        ValidatedReviewRequest request,
        CancellationToken cancellationToken)
    {
        using var handler = handlerFactory(request.Mapping);
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        ApplyAuthentication(client, request.Mapping);

        var basePath = BuildRepositoryApiBase(request);
        using var pullRequest = await GetJsonAsync(
            client,
            $"{basePath}/pullrequests/{request.PullRequestId}?api-version={Escape(request.Mapping.ApiVersion)}",
            cancellationToken).ConfigureAwait(false);

        var root = pullRequest.RootElement;
        var repositoryId = GetRequiredString(root, "repository", "id");
        var sourceRefName = GetRequiredString(root, "sourceRefName");
        var targetRefName = GetRequiredString(root, "targetRefName");

        using var iterations = await GetJsonAsync(
            client,
            $"{basePath}/pullrequests/{request.PullRequestId}/iterations?api-version={Escape(request.Mapping.ApiVersion)}",
            cancellationToken).ConfigureAwait(false);

        var latestIteration = GetLatestIteration(iterations.RootElement);
        var sourceCommit = latestIteration is null
            ? GetRequiredString(root, "lastMergeSourceCommit", "commitId")
            : GetRequiredString(latestIteration.Value, "sourceRefCommit", "commitId");
        var targetCommit = latestIteration is null
            ? GetRequiredString(root, "lastMergeTargetCommit", "commitId")
            : GetRequiredString(latestIteration.Value, "targetRefCommit", "commitId");

        ValidateCommit(sourceCommit, "source");
        ValidateCommit(targetCommit, "target");

        return new PullRequestContext(
            repositoryId,
            request.PullRequestId,
            sourceRefName,
            targetRefName,
            sourceCommit.ToLowerInvariant(),
            targetCommit.ToLowerInvariant(),
            latestIteration is null ? null : latestIteration.Value.GetProperty("id").GetInt32());
    }

    public async Task<PublishedComment> PublishReviewAsync(
        ValidatedReviewRequest request,
        PullRequestContext pullRequest,
        string answer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > MaxPublishedCommentLength)
        {
            throw new AzureDevOpsRequestException(
                "invalid_review_answer",
                $"Review answer must contain between 1 and {MaxPublishedCommentLength} characters.");
        }

        using var handler = handlerFactory(request.Mapping);
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        ApplyAuthentication(client, request.Mapping);

        var basePath = BuildRepositoryApiBase(request);
        var threadContext = await TryBuildThreadContextAsync(
            client,
            basePath,
            request,
            pullRequest,
            cancellationToken).ConfigureAwait(false);
        var content = $"""
            **Codex 辅助评审** · `{request.FilePath}:{request.StartLine}-{request.EndLine}`

            {answer}

            ---
            Source commit: `{pullRequest.SourceCommit}`
            """;
        var body = new Dictionary<string, object?>
        {
            ["comments"] = new[]
            {
                new
                {
                    parentCommentId = 0,
                    content,
                    commentType = 1,
                },
            },
            ["status"] = 1,
        };
        if (threadContext is not null)
        {
            body["threadContext"] = new
            {
                filePath = request.FilePath,
                rightFileStart = new { line = request.StartLine, offset = 1 },
                rightFileEnd = new { line = request.EndLine, offset = 1 },
            };
            body["pullRequestThreadContext"] = new
            {
                changeTrackingId = threadContext.ChangeTrackingId,
                iterationContext = new
                {
                    firstComparingIteration = 1,
                    secondComparingIteration = threadContext.IterationId,
                },
            };
        }

        using var response = await PostJsonAsync(
            client,
            $"{basePath}/pullrequests/{request.PullRequestId}/threads?api-version={Escape(request.Mapping.ApiVersion)}",
            body,
            cancellationToken).ConfigureAwait(false);
        var threadId = response.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var value)
            ? value
            : 0;
        return new PublishedComment(threadId, threadContext is not null);
    }

    private static HttpClientHandler CreateHandler(RepositoryMapping mapping) => new()
    {
        UseDefaultCredentials = mapping.AuthMode == AzureDevOpsAuthMode.Windows,
        Credentials = mapping.AuthMode == AzureDevOpsAuthMode.Windows
            ? CredentialCache.DefaultNetworkCredentials
            : null,
        PreAuthenticate = true,
    };

    private static void ApplyAuthentication(HttpClient client, RepositoryMapping mapping)
    {
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (mapping.AuthMode != AzureDevOpsAuthMode.Pat)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mapping.PatEnvironmentVariable))
        {
            throw new BridgeConfigurationException(
                "PAT authentication requires patEnvironmentVariable in the repository mapping.");
        }

        var pat = Environment.GetEnvironmentVariable(mapping.PatEnvironmentVariable);
        if (string.IsNullOrEmpty(pat))
        {
            throw new BridgeConfigurationException(
                $"PAT environment variable '{mapping.PatEnvironmentVariable}' is not set.");
        }

        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    private static string BuildRepositoryApiBase(ValidatedReviewRequest request)
    {
        var server = request.ServerUri.AbsoluteUri.TrimEnd('/');
        return $"{server}/{Escape(request.Collection)}/{Escape(request.Project)}/_apis/git/repositories/{Escape(request.Repository)}";
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureDevOpsRequestException(
                "azure_devops_request_failed",
                $"Azure DevOps returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new AzureDevOpsRequestException(
                "azure_devops_invalid_response",
                "Azure DevOps returned invalid JSON.",
                exception);
        }
    }

    private static async Task<JsonDocument> PostJsonAsync(
        HttpClient client,
        string url,
        object body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, BridgeJson.SerializerOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureDevOpsRequestException(
                "azure_devops_publish_failed",
                $"Azure DevOps returned HTTP {(int)response.StatusCode} while publishing the review.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PullRequestFileContext?> TryBuildThreadContextAsync(
        HttpClient client,
        string basePath,
        ValidatedReviewRequest request,
        PullRequestContext pullRequest,
        CancellationToken cancellationToken)
    {
        if (pullRequest.IterationId is null)
        {
            return null;
        }

        using var changes = await GetJsonAsync(
            client,
            $"{basePath}/pullrequests/{request.PullRequestId}/iterations/{pullRequest.IterationId}/changes?$top=2000&api-version={Escape(request.Mapping.ApiVersion)}",
            cancellationToken).ConfigureAwait(false);
        if (!changes.RootElement.TryGetProperty("changeEntries", out var entries) &&
            !changes.RootElement.TryGetProperty("value", out entries))
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("item", out var item) ||
                !item.TryGetProperty("path", out var path) ||
                !string.Equals(path.GetString(), request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                !entry.TryGetProperty("changeTrackingId", out var changeTrackingId) ||
                !changeTrackingId.TryGetInt32(out var id))
            {
                continue;
            }

            return new PullRequestFileContext(pullRequest.IterationId.Value, id);
        }

        return null;
    }

    private static JsonElement? GetLatestIteration(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new AzureDevOpsRequestException(
                "azure_devops_invalid_response",
                "Azure DevOps iteration response did not contain an array.");
        }

        JsonElement? latest = null;
        var latestId = int.MinValue;
        foreach (var value in values.EnumerateArray())
        {
            if (value.TryGetProperty("id", out var idElement) &&
                idElement.TryGetInt32(out var id) &&
                id > latestId)
            {
                latestId = id;
                latest = value.Clone();
            }
        }

        return latest;
    }

    private static string GetRequiredString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                throw new AzureDevOpsRequestException(
                    "azure_devops_invalid_response",
                    $"Azure DevOps response omitted '{string.Join('.', path)}'.");
            }
        }

        return current.GetString() ?? throw new AzureDevOpsRequestException(
            "azure_devops_invalid_response",
            $"Azure DevOps response contained an empty '{string.Join('.', path)}'.");
    }

    private static void ValidateCommit(string commit, string name)
    {
        if (commit.Length != 40 || commit.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new AzureDevOpsRequestException(
                "azure_devops_invalid_response",
                $"Azure DevOps returned an invalid {name} commit ID.");
        }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

public sealed record PullRequestContext(
    string RepositoryId,
    int PullRequestId,
    string SourceRefName,
    string TargetRefName,
    string SourceCommit,
    string TargetCommit,
    int? IterationId);

public sealed record PublishedComment(int ThreadId, bool IsInline);

internal sealed record PullRequestFileContext(int IterationId, int ChangeTrackingId);

public sealed class AzureDevOpsRequestException : Exception
{
    public AzureDevOpsRequestException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AzureDevOpsRequestException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
