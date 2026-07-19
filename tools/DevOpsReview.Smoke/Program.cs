using System.Diagnostics;
using System.Text;
using DevOpsReview.Bridge.Codex;

namespace DevOpsReview.Smoke;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var workingDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) : Environment.CurrentDirectory;
        if (!Directory.Exists(workingDirectory))
        {
            Console.Error.WriteLine($"Working directory does not exist: {workingDirectory}");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var codex = new CodexAppServerClient("codex");
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstDelta = null;
        var answer = new StringBuilder();

        try
        {
            var threadId = await codex.StartThreadAsync(
                workingDirectory,
                timeout.Token,
                ephemeral: true).ConfigureAwait(false);
            var result = await codex.RunTurnAsync(
                threadId,
                "这是协议连通性检查。不要读取文件，不要运行命令，只回复 READY。",
                (streamEvent, _) =>
                {
                    if (streamEvent.Kind == CodexStreamEventKind.Delta)
                    {
                        firstDelta ??= stopwatch.Elapsed;
                        answer.Append(streamEvent.Text);
                    }

                    return Task.CompletedTask;
                },
                timeout.Token).ConfigureAwait(false);

            stopwatch.Stop();
            Console.WriteLine(answer.ToString());
            Console.WriteLine($"thread={threadId}");
            Console.WriteLine($"turn={result.TurnId}");
            Console.WriteLine($"first_delta_ms={firstDelta?.TotalMilliseconds:F0}");
            Console.WriteLine($"total_ms={stopwatch.Elapsed.TotalMilliseconds:F0}");
            return answer.ToString().Contains("READY", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
