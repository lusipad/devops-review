using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.NativeHostSmoke;

public static class Program
{
    private const string ExtensionOrigin = "chrome-extension://kldpfliioeaahafemncagclpehbnblig/";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: DevOpsReview.NativeHostSmoke <absolute-config-path>");
            return 2;
        }

        var configPath = Path.GetFullPath(args[0]);
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Configuration does not exist: {configPath}");
            return 2;
        }

        var bridgeAssembly = typeof(DevOpsReview.Bridge.Program).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(bridgeAssembly);
        startInfo.ArgumentList.Add(ExtensionOrigin);
        startInfo.ArgumentList.Add("--parent-window=0");
        startInfo.Environment["DEVOPS_REVIEW_CONFIG"] = configPath;

        using var bridge = new Process { StartInfo = startInfo };
        if (!bridge.Start())
        {
            Console.Error.WriteLine("Bridge process could not be started.");
            return 1;
        }

        var stderr = CopyErrorsAsync(bridge.StandardError);
        var requestId = Guid.NewGuid().ToString();
        var request = new
        {
            type = BridgeMessageTypes.ReviewStart,
            requestId,
            payload = new
            {
                serverUrl = "http://localhost:8081",
                collection = "DefaultCollection",
                project = "test",
                repository = "test",
                pullRequestId = 4,
                filePath = "/src/tax.js",
                startLine = 4,
                endLine = 6,
                selectedText = "function applyTax(total, ratePercent) { ... }",
                question = "结合完整仓库解释这个计税函数，并检查它是否存在可实际触发的正确性问题。只报告有证据的问题。",
            },
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await WriteFrameAsync(bridge.StandardInput.BaseStream, request, timeout.Token).ConfigureAwait(false);
            while (true)
            {
                using var response = await ReadFrameAsync(bridge.StandardOutput.BaseStream, timeout.Token)
                    .ConfigureAwait(false);
                Console.WriteLine(response.RootElement.GetRawText());
                var type = response.RootElement.GetProperty("type").GetString();
                var responseRequestId = response.RootElement.GetProperty("requestId").GetString();
                if (responseRequestId != requestId)
                {
                    continue;
                }

                if (type == BridgeMessageTypes.ReviewCompleted)
                {
                    bridge.StandardInput.Close();
                    await bridge.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    await stderr.ConfigureAwait(false);
                    return bridge.ExitCode;
                }

                if (type == BridgeMessageTypes.ReviewFailed)
                {
                    bridge.StandardInput.Close();
                    await bridge.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    await stderr.ConfigureAwait(false);
                    return 1;
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (!bridge.HasExited)
            {
                bridge.Kill(entireProcessTree: true);
            }

            await stderr.ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task WriteFrameAsync(
        Stream stream,
        object message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJson.SerializerOptions);
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(uint)];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length is 0 or > 1024 * 1024)
        {
            throw new InvalidDataException($"Invalid native response length: {length}");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Native host exited before sending a complete frame.");
            }

            read += count;
        }
    }

    private static async Task CopyErrorsAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            Console.Error.WriteLine($"bridge: {line}");
        }
    }
}
