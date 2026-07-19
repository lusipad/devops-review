using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevOpsReview.Bridge.Protocol;

public static class BridgeMessageTypes
{
    public const string ReviewStart = "review.start";
    public const string ReviewCancel = "review.cancel";
    public const string ReviewPublish = "review.publish";
    public const string HostStatus = "host.status";

    public const string ReviewAccepted = "review.accepted";
    public const string ReviewProgress = "review.progress";
    public const string ReviewDelta = "review.delta";
    public const string ReviewCompleted = "review.completed";
    public const string ReviewFailed = "review.failed";
    public const string ReviewCancelled = "review.cancelled";
    public const string ReviewPublished = "review.published";
}

public sealed record BridgeRequestEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("payload")] JsonElement Payload);

public sealed record ReviewStartRequest(
    [property: JsonPropertyName("serverUrl")] string ServerUrl,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("pullRequestId")] int PullRequestId,
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("startLine")] int StartLine,
    [property: JsonPropertyName("endLine")] int EndLine,
    [property: JsonPropertyName("selectedText")] string SelectedText,
    [property: JsonPropertyName("question")] string Question);

public sealed record ReviewCancelRequest(
    [property: JsonPropertyName("targetRequestId")] string TargetRequestId);

public sealed record ReviewPublishRequest(
    [property: JsonPropertyName("targetRequestId")] string TargetRequestId);

public sealed record BridgeResponseEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("payload")] object? Payload = null);

public sealed record BridgeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public static class BridgeJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
