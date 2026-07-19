using System.Diagnostics;
namespace DevOpsReview.Bridge.Git;

public interface IGitProcessRunner
{
    Task<GitCommandResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class GitProcessRunner : IGitProcessRunner
{
    public async Task<GitCommandResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"safe.directory={Path.GetFullPath(repositoryPath)}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("http.emptyAuth=true");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git process could not be started.");
        }

        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new GitCommandException("Git command timed out.");
            }

            throw;
        }

        var stdout = Limit(await stdoutTask.ConfigureAwait(false), 128 * 1024);
        var stderr = Limit(await stderrTask.ConfigureAwait(false), 128 * 1024);
        if (process.ExitCode != 0)
        {
            throw new GitCommandException(
                $"Git command failed with exit code {process.ExitCode}.",
                stderr);
        }

        return new GitCommandResult(stdout.Trim(), stderr.Trim());
    }

    private static string Limit(string value, int limit) =>
        value.Length <= limit ? value : value[..limit];
}

public sealed record GitCommandResult(string StandardOutput, string StandardError);

public sealed class GitCommandException : Exception
{
    public GitCommandException(string message, string? diagnostic = null)
        : base(message)
    {
        Diagnostic = diagnostic;
    }

    public string? Diagnostic { get; }
}
