using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Bridge.Codex;

public sealed class CodexAppServerClient(
    string codexExecutable,
    IReadOnlyList<string>? leadingArguments = null) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingRequests = new();
    private readonly ConcurrentDictionary<string, TurnObserver> activeTurns = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim startLock = new(1, 1);
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private StreamWriter? input;
    private Task? readLoop;
    private Task? errorLoop;
    private long requestId;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (process is { HasExited: false })
        {
            return;
        }

        await startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (process is { HasExited: false })
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = codexExecutable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CreateNoWindow = true,
            };
            foreach (var argument in leadingArguments ?? [])
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new CodexAppServerException("codex_start_failed", "Codex App Server could not be started.");
            }

            input = process.StandardInput;
            input.AutoFlush = true;
            readLoop = ReadLoopAsync(process.StandardOutput, lifetime.Token);
            errorLoop = CopyErrorsAsync(process.StandardError, lifetime.Token);

            var result = await SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "azure_devops_codex_review",
                        title = "Azure DevOps Codex Review",
                        version = "0.1.0",
                    },
                },
                cancellationToken).ConfigureAwait(false);
            _ = result;
            await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally
        {
            startLock.Release();
        }
    }

    public async Task<string> StartThreadAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool ephemeral = false)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var result = await SendRequestAsync(
            "thread/start",
            new
            {
                cwd = workingDirectory,
                approvalPolicy = "never",
                sandbox = "read-only",
                serviceName = "azure_devops_review",
                ephemeral,
            },
            cancellationToken).ConfigureAwait(false);
        return GetNestedString(result, "thread", "id");
    }

    public async Task ResumeThreadAsync(
        string threadId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await SendRequestAsync(
            "thread/resume",
            new
            {
                threadId,
                cwd = workingDirectory,
                approvalPolicy = "never",
                sandbox = "read-only",
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CodexTurnResult> RunTurnAsync(
        string threadId,
        string prompt,
        Func<CodexStreamEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var observer = new TurnObserver(onEvent);
        if (!activeTurns.TryAdd(threadId, observer))
        {
            throw new CodexAppServerException(
                "codex_thread_busy",
                "This PR already has an active Codex turn.");
        }

        try
        {
            try
            {
                var result = await SendRequestAsync(
                    "turn/start",
                    new
                    {
                        threadId,
                        input = new[] { new { type = "text", text = prompt } },
                        approvalPolicy = "never",
                        sandboxPolicy = new { type = "readOnly" },
                    },
                    cancellationToken).ConfigureAwait(false);
                observer.TurnId = GetNestedString(result, "turn", "id");
                return await observer.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TryInterruptAsync(threadId, observer.TurnId).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            activeTurns.TryRemove(threadId, out _);
        }
    }

    private async Task TryInterruptAsync(string threadId, string? turnId)
    {
        if (string.IsNullOrEmpty(turnId))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await SendRequestAsync(
                "turn/interrupt",
                new { threadId, turnId },
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine($"Codex interrupt failed: {exception.GetType().Name}");
        }
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingRequests.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Duplicate Codex request ID.");
        }

        try
        {
            await WriteAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pendingRequests.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var writer = input ?? throw new CodexAppServerException(
            "codex_not_running",
            "Codex App Server is not running.");
        var json = JsonSerializer.Serialize(message, BridgeJson.SerializerOptions);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new CodexAppServerException(
                "codex_transport_failed",
                "Codex App Server connection was lost.",
                exception);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    CompleteRequest(id, root);
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement) &&
                    root.TryGetProperty("params", out var paramsElement))
                {
                    await DispatchNotificationAsync(
                        methodElement.GetString() ?? string.Empty,
                        paramsElement.Clone(),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            var exception = failure is null
                ? new CodexAppServerException("codex_exited", "Codex App Server exited unexpectedly.")
                : new CodexAppServerException(
                    "codex_transport_failed",
                    "Codex App Server returned an invalid or interrupted stream.",
                    failure);
            FailPending(exception);
        }
    }

    private void CompleteRequest(long id, JsonElement root)
    {
        if (!pendingRequests.TryRemove(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "Unknown Codex App Server error."
                : "Unknown Codex App Server error.";
            completion.TrySetException(new CodexAppServerException("codex_request_failed", message));
            return;
        }

        if (!root.TryGetProperty("result", out var result))
        {
            completion.TrySetException(new CodexAppServerException(
                "codex_invalid_response",
                "Codex App Server response omitted both result and error."));
            return;
        }

        completion.TrySetResult(result.Clone());
    }

    private async Task DispatchNotificationAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("threadId", out var threadIdElement))
        {
            return;
        }

        var threadId = threadIdElement.GetString();
        if (threadId is null || !activeTurns.TryGetValue(threadId, out var observer))
        {
            return;
        }

        switch (method)
        {
            case "turn/started":
                if (parameters.TryGetProperty("turn", out var startedTurn) &&
                    startedTurn.TryGetProperty("id", out var startedTurnId))
                {
                    observer.TurnId ??= startedTurnId.GetString();
                }

                await observer.OnEvent(
                    new CodexStreamEvent(CodexStreamEventKind.Progress, "Codex 已开始分析。"),
                    cancellationToken).ConfigureAwait(false);
                break;

            case "item/started":
                RememberAgentMessagePhase(observer, parameters);
                break;

            case "item/agentMessage/delta":
                if (ShouldForwardAgentMessageDelta(observer, parameters) &&
                    parameters.TryGetProperty("delta", out var deltaElement))
                {
                    var delta = deltaElement.GetString();
                    if (!string.IsNullOrEmpty(delta))
                    {
                        await observer.OnEvent(
                            new CodexStreamEvent(CodexStreamEventKind.Delta, delta),
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                break;

            case "turn/completed":
                CompleteTurn(observer, parameters);
                break;
        }
    }

    private static void RememberAgentMessagePhase(TurnObserver observer, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item) ||
            !item.TryGetProperty("type", out var type) ||
            type.GetString() != "agentMessage" ||
            !item.TryGetProperty("id", out var idElement) ||
            idElement.GetString() is not { } itemId)
        {
            return;
        }

        var phase = item.TryGetProperty("phase", out var phaseElement)
            ? phaseElement.GetString()
            : null;
        observer.MessagePhases[itemId] = phase ?? string.Empty;
    }

    private static bool ShouldForwardAgentMessageDelta(
        TurnObserver observer,
        JsonElement parameters)
    {
        if (!parameters.TryGetProperty("itemId", out var itemIdElement) ||
            itemIdElement.GetString() is not { } itemId ||
            !observer.MessagePhases.TryGetValue(itemId, out var phase))
        {
            return true;
        }

        return phase != "commentary";
    }

    private static void CompleteTurn(TurnObserver observer, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("turn", out var turn))
        {
            observer.Completion.TrySetException(new CodexAppServerException(
                "codex_invalid_response",
                "Codex completion event omitted turn data."));
            return;
        }

        var id = turn.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? observer.TurnId ?? string.Empty
            : observer.TurnId ?? string.Empty;
        var status = turn.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "unknown"
            : "unknown";

        if (status is "completed")
        {
            observer.Completion.TrySetResult(new CodexTurnResult(id, status));
            return;
        }

        if (status is "interrupted")
        {
            observer.Completion.TrySetCanceled();
            return;
        }

        observer.Completion.TrySetException(new CodexAppServerException(
            "codex_turn_failed",
            $"Codex turn ended with status '{status}'."));
    }

    private static string GetNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                throw new CodexAppServerException(
                    "codex_invalid_response",
                    $"Codex response omitted '{string.Join('.', path)}'.");
            }
        }

        return current.GetString() ?? throw new CodexAppServerException(
            "codex_invalid_response",
            $"Codex response contained an empty '{string.Join('.', path)}'.");
    }

    private static async Task CopyErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                Console.Error.WriteLine($"codex: {line}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in pendingRequests.Values)
        {
            completion.TrySetException(exception);
        }

        foreach (var observer in activeTurns.Values)
        {
            observer.Completion.TrySetException(exception);
        }
    }

    private void StopProcess()
    {
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        StopProcess();

        foreach (var task in new[] { readLoop, errorLoop })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
            }
        }

        process?.Dispose();
        lifetime.Dispose();
        startLock.Dispose();
        writeLock.Dispose();
    }

    private sealed class TurnObserver(
        Func<CodexStreamEvent, CancellationToken, Task> onEvent)
    {
        public string? TurnId { get; set; }

        public Func<CodexStreamEvent, CancellationToken, Task> OnEvent { get; } = onEvent;

        public ConcurrentDictionary<string, string> MessagePhases { get; } = new(StringComparer.Ordinal);

        public TaskCompletionSource<CodexTurnResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public enum CodexStreamEventKind
{
    Progress,
    Delta,
}

public sealed record CodexStreamEvent(CodexStreamEventKind Kind, string Text);

public sealed record CodexTurnResult(string TurnId, string Status);

public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public CodexAppServerException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
