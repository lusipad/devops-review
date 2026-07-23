using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Git;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Configurator;

internal sealed class ConfigurationService
{
    private const string ExtensionOrigin =
        "chrome-extension://kldpfliioeaahafemncagclpehbnblig/";
    private readonly string configPath;
    private BridgeOptions? existingOptions;

    public ConfigurationService()
    {
        configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsReview",
            "config.json");
    }

    public string ConfigPath => configPath;

    public async Task<ConfigurationValues?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(configPath);
        existingOptions = await JsonSerializer.DeserializeAsync<BridgeOptions>(
            stream,
            BridgeJson.SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (existingOptions is null || existingOptions.Repositories.Count == 0)
        {
            return null;
        }

        var repository = existingOptions.Repositories[0];
        var locator = new AzureDevOpsRepositoryLocator(
            repository.ServerUrl,
            repository.Collection,
            repository.Project,
            repository.Repository);
        return new ConfigurationValues(locator.ToRepositoryUrl(), repository.LocalPath);
    }

    public static async Task<TestResult> TestAsync(
        string azureDevOpsUrl,
        string localPath,
        CancellationToken cancellationToken)
    {
        var locator = AzureDevOpsRepositoryLocator.Parse(azureDevOpsUrl);
        var repositoryPath = Path.GetFullPath(localPath);
        if (!Directory.Exists(repositoryPath))
        {
            throw new InvalidOperationException("所选本地仓库目录不存在。");
        }

        var git = new GitProcessRunner();
        var root = await git.RunAsync(
            repositoryPath,
            ["rev-parse", "--show-toplevel"],
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        var actualRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.StandardOutput));
        if (!string.Equals(
                actualRoot,
                Path.TrimEndingDirectorySeparator(repositoryPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"请选择仓库根目录：{actualRoot}");
        }

        var remote = await git.RunAsync(
            repositoryPath,
            ["remote", "get-url", "origin"],
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (!RemoteMatchesRepository(remote.StandardOutput, locator))
        {
            throw new InvalidOperationException(
                $"origin 指向“{remote.StandardOutput}”，与仓库“{locator.Repository}”不匹配。");
        }

        var mapping = CreateMapping(locator, repositoryPath);
        await new AzureDevOpsClient()
            .TestConnectionAsync(mapping, cancellationToken)
            .ConfigureAwait(false);

        var codexExecutable = DiscoverCodexExecutable();
        await VerifyCodexLoginAsync(codexExecutable, cancellationToken).ConfigureAwait(false);
        return new TestResult(locator, repositoryPath, remote.StandardOutput, codexExecutable);
    }

    public async Task SaveAsync(TestResult result, CancellationToken cancellationToken)
    {
        var dataDirectory = existingOptions?.DataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsReview");
        var worktreeRoot = existingOptions?.WorktreeRoot ?? Path.Combine(dataDirectory, "worktrees");
        var repositories = existingOptions?.Repositories.ToList() ?? [];
        var mapping = CreateMapping(result.Locator, result.LocalPath);
        if (repositories.Count == 0)
        {
            repositories.Add(mapping);
        }
        else
        {
            repositories[0] = mapping;
        }

        var options = new BridgeOptions
        {
            DataDirectory = dataDirectory,
            WorktreeRoot = worktreeRoot,
            CodexExecutable = result.CodexExecutable,
            AllowedExtensionOrigins = [ExtensionOrigin],
            Repositories = repositories,
        };
        BridgeOptionsValidator.Validate(options);

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var temporaryPath = $"{configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    options,
                    BridgeJson.SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, configPath, true);
            existingOptions = options;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static RepositoryMapping CreateMapping(
        AzureDevOpsRepositoryLocator locator,
        string localPath) => new()
        {
            ServerUrl = locator.ServerUrl,
            Collection = locator.Collection,
            Project = locator.Project,
            Repository = locator.Repository,
            LocalPath = localPath,
            AuthMode = AzureDevOpsAuthMode.Windows,
            ApiVersion = "7.0",
        };

    private static bool RemoteMatchesRepository(
        string remote,
        AzureDevOpsRepositoryLocator locator)
    {
        var trimmed = remote.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        try
        {
            var remoteLocator = AzureDevOpsRepositoryLocator.Parse(trimmed);
            return remoteLocator.IdentifiesSameRepository(locator);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string DiscoverCodexExecutable()
    {
        var npmCommand = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm",
            "codex.cmd");
        if (File.Exists(npmCommand))
        {
            return npmCommand;
        }

        foreach (var name in new[] { "codex.exe", "codex.cmd" })
        {
            var path = FindOnPath(name);
            if (path is not null)
            {
                return path;
            }
        }

        throw new InvalidOperationException("未找到 Codex CLI，请先安装并登录 Codex。");
    }

    private static string? FindOnPath(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task VerifyCodexLoginAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("login");
        startInfo.ArgumentList.Add("status");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 Codex CLI。");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new InvalidOperationException("检查 Codex 登录状态超时。");
        }

        var output = $"{await stdout.ConfigureAwait(false)}{await stderr.ConfigureAwait(false)}";
        if (process.ExitCode != 0 ||
            !output.Contains("Logged in", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Codex 尚未登录，请先运行 codex login。");
        }
    }
}

internal sealed record ConfigurationValues(string AzureDevOpsUrl, string LocalPath);

internal sealed record TestResult(
    AzureDevOpsRepositoryLocator Locator,
    string LocalPath,
    string RemoteUrl,
    string CodexExecutable);
