using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Codex;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Git;
using DevOpsReview.Bridge.Orchestration;
using DevOpsReview.Bridge.Protocol;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.NativeMessaging;

public sealed class NativeMessagingHost(
    NativeMessageTransport transport,
    ReviewRequestValidator validator,
    ReviewOrchestrator orchestrator) : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeRequests =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim outputLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CompletedReview> completedReviews = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> publishedReviews = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var activeTasks = new ConcurrentBag<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await transport.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (!IsValidRequestId(message.RequestId))
                {
                    await WriteAsync(
                        new BridgeResponseEnvelope(
                            BridgeMessageTypes.ReviewFailed,
                            message.RequestId,
                            new BridgeError("invalid_request_id", "requestId must be a UUID.")),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (message.Type)
                {
                    case BridgeMessageTypes.ReviewStart:
                        var task = StartReviewAsync(message, cancellationToken);
                        activeTasks.Add(task);
                        break;
                    case BridgeMessageTypes.ReviewCancel:
                        await CancelReviewAsync(message, cancellationToken).ConfigureAwait(false);
                        break;
                    case BridgeMessageTypes.ReviewPublish:
                        await PublishReviewAsync(message, cancellationToken).ConfigureAwait(false);
                        break;
                    case BridgeMessageTypes.HostStatus:
                        await WriteAsync(
                            new BridgeResponseEnvelope(
                                BridgeMessageTypes.HostStatus,
                                message.RequestId,
                                new { ready = true, version = "0.1.0" }),
                            cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await WriteAsync(
                            new BridgeResponseEnvelope(
                                BridgeMessageTypes.ReviewFailed,
                                message.RequestId,
                                new BridgeError("unsupported_message", "Unsupported message type.")),
                            cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        finally
        {
            foreach (var source in activeRequests.Values)
            {
                source.Cancel();
            }

            await Task.WhenAll(activeTasks.ToArray()).ConfigureAwait(false);
            foreach (var source in activeRequests.Values)
            {
                source.Dispose();
            }

        }
    }

    private async Task StartReviewAsync(BridgeRequestEnvelope message, CancellationToken hostCancellationToken)
    {
        if (activeRequests.ContainsKey(message.RequestId))
        {
            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewFailed,
                    message.RequestId,
                    new BridgeError("duplicate_request", "A request with this ID is already active.")),
                hostCancellationToken).ConfigureAwait(false);
            return;
        }

        ReviewStartRequest request;
        ValidatedReviewRequest validated;
        try
        {
            request = message.Payload.Deserialize<ReviewStartRequest>(BridgeJson.SerializerOptions)
                ?? throw new JsonException("Review payload was empty.");
            validated = validator.Validate(request);
        }
        catch (ReviewRequestValidationException exception)
        {
            await WriteFailureAsync(message.RequestId, exception.Code, exception.Message, hostCancellationToken)
                .ConfigureAwait(false);
            return;
        }
        catch (JsonException)
        {
            await WriteFailureAsync(
                message.RequestId,
                "invalid_payload",
                "Review request payload is invalid.",
                hostCancellationToken).ConfigureAwait(false);
            return;
        }

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        if (!activeRequests.TryAdd(message.RequestId, requestCancellation))
        {
            await WriteFailureAsync(
                message.RequestId,
                "duplicate_request",
                "A request with this ID is already active.",
                hostCancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var answer = new StringBuilder();
            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewAccepted,
                    message.RequestId,
                    new { accepted = true }),
                requestCancellation.Token).ConfigureAwait(false);

            var result = await orchestrator.RunAsync(
                validated,
                async (executionEvent, eventCancellationToken) =>
                {
                    var type = executionEvent.Kind == ReviewExecutionEventKind.Delta
                        ? BridgeMessageTypes.ReviewDelta
                        : BridgeMessageTypes.ReviewProgress;
                    var propertyName = executionEvent.Kind == ReviewExecutionEventKind.Delta
                        ? "delta"
                        : "message";
                    if (executionEvent.Kind == ReviewExecutionEventKind.Delta)
                    {
                        answer.Append(executionEvent.Text);
                    }
                    await WriteAsync(
                        new BridgeResponseEnvelope(
                            type,
                            message.RequestId,
                            new Dictionary<string, string> { [propertyName] = executionEvent.Text }),
                        eventCancellationToken).ConfigureAwait(false);
                },
                requestCancellation.Token).ConfigureAwait(false);

            var sanitizedAnswer = ReviewOutputSanitizer.Sanitize(answer.ToString(), result.WorktreePath);
            completedReviews[message.RequestId] = new CompletedReview(validated, result, sanitizedAnswer);

            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewCompleted,
                    message.RequestId,
                    new
                    {
                        result.SessionKey,
                        result.ThreadId,
                        result.TurnId,
                        result.SourceCommit,
                        result.TargetCommit,
                        answer = sanitizedAnswer,
                    }),
                requestCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewCancelled,
                    message.RequestId,
                    new { cancelled = true }),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            var (code, safeMessage) = ToSafeError(exception);
            Console.Error.WriteLine($"Review {message.RequestId} failed: {exception}");
            await WriteFailureAsync(message.RequestId, code, safeMessage, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            activeRequests.TryRemove(message.RequestId, out _);
        }
    }

    private async Task PublishReviewAsync(
        BridgeRequestEnvelope message,
        CancellationToken cancellationToken)
    {
        ReviewPublishRequest request;
        try
        {
            request = message.Payload.Deserialize<ReviewPublishRequest>(BridgeJson.SerializerOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            await WriteFailureAsync(
                message.RequestId,
                "invalid_payload",
                "Publish request payload is invalid.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!completedReviews.TryGetValue(request.TargetRequestId, out var completed))
        {
            await WriteFailureAsync(
                message.RequestId,
                "review_not_available",
                "Completed review is no longer available to publish.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!publishedReviews.TryAdd(request.TargetRequestId, 0))
        {
            await WriteFailureAsync(
                message.RequestId,
                "review_already_published",
                "This review was already published.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var published = await orchestrator.PublishAsync(
                completed.Request,
                completed.Result,
                completed.Answer,
                cancellationToken).ConfigureAwait(false);
            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewPublished,
                    message.RequestId,
                    new
                    {
                        targetRequestId = request.TargetRequestId,
                        published.ThreadId,
                        published.IsInline,
                    }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            publishedReviews.TryRemove(request.TargetRequestId, out _);
            var (code, safeMessage) = ToSafeError(exception);
            Console.Error.WriteLine($"Publish {message.RequestId} failed: {exception}");
            await WriteFailureAsync(message.RequestId, code, safeMessage, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task CancelReviewAsync(BridgeRequestEnvelope message, CancellationToken cancellationToken)
    {
        try
        {
            var request = message.Payload.Deserialize<ReviewCancelRequest>(BridgeJson.SerializerOptions)
                ?? throw new JsonException();
            if (activeRequests.TryGetValue(request.TargetRequestId, out var source))
            {
                source.Cancel();
            }

            await WriteAsync(
                new BridgeResponseEnvelope(
                    BridgeMessageTypes.ReviewCancelled,
                    message.RequestId,
                    new { targetRequestId = request.TargetRequestId, cancellationRequested = source is not null }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteFailureAsync(
                message.RequestId,
                "invalid_payload",
                "Cancel request payload is invalid.",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteFailureAsync(
        string requestId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        await WriteAsync(
            new BridgeResponseEnvelope(
                BridgeMessageTypes.ReviewFailed,
                requestId,
                new BridgeError(code, message)),
            cancellationToken).ConfigureAwait(false);

    private async Task WriteAsync(BridgeResponseEnvelope response, CancellationToken cancellationToken)
    {
        await outputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await transport.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            outputLock.Release();
        }
    }

    private static bool IsValidRequestId(string requestId) =>
        requestId.Length <= 64 && Guid.TryParse(requestId, out _);

    private static (string Code, string Message) ToSafeError(Exception exception) => exception switch
    {
        AzureDevOpsRequestException azure => (azure.Code, azure.Message),
        WorktreeException worktree => (worktree.Code, worktree.Message),
        GitCommandException => ("git_failed", "Git could not prepare the requested PR worktree."),
        CodexAppServerException codex => (codex.Code, codex.Message),
        BridgeConfigurationException => ("configuration_invalid", "Bridge configuration is invalid."),
        _ => ("review_failed", "The review could not be completed."),
    };

    public void Dispose()
    {
        outputLock.Dispose();
        foreach (var source in activeRequests.Values)
        {
            source.Dispose();
        }
    }

    private sealed record CompletedReview(
        ValidatedReviewRequest Request,
        ReviewExecutionResult Result,
        string Answer);
}
