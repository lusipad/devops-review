using System.Text.RegularExpressions;

namespace DevOpsReview.Bridge.Configuration;

public static partial class BridgeOptionsValidator
{
    public static void Validate(BridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateDirectory(options.DataDirectory, nameof(options.DataDirectory));
        var worktreeRoot = ValidateDirectory(options.WorktreeRoot, nameof(options.WorktreeRoot));

        if (string.IsNullOrWhiteSpace(options.CodexExecutable))
        {
            throw new BridgeConfigurationException("codexExecutable must not be empty.");
        }

        if (options.AllowedExtensionOrigins.Count == 0 ||
            options.AllowedExtensionOrigins.Any(origin => !ExtensionOriginPattern().IsMatch(origin)))
        {
            throw new BridgeConfigurationException(
                "allowedExtensionOrigins must contain exact chrome-extension://<32-character-id>/ origins.");
        }

        if (options.Repositories.Count == 0)
        {
            throw new BridgeConfigurationException("At least one repository mapping is required.");
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in options.Repositories)
        {
            if (!Uri.TryCreate(repository.ServerUrl, UriKind.Absolute, out var server) ||
                server.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(server.UserInfo) ||
                !string.IsNullOrEmpty(server.Query) ||
                !string.IsNullOrEmpty(server.Fragment))
            {
                throw new BridgeConfigurationException(
                    $"Repository '{repository.Repository}' has an invalid serverUrl.");
            }

            if (new[] { repository.Collection, repository.Project, repository.Repository }
                .Any(string.IsNullOrWhiteSpace))
            {
                throw new BridgeConfigurationException("Repository locator values must not be empty.");
            }

            if (!ApiVersionPattern().IsMatch(repository.ApiVersion))
            {
                throw new BridgeConfigurationException(
                    $"Repository '{repository.Repository}' has an invalid apiVersion.");
            }

            if (repository.AuthMode == AzureDevOpsAuthMode.Pat &&
                (string.IsNullOrWhiteSpace(repository.PatEnvironmentVariable) ||
                 !EnvironmentVariablePattern().IsMatch(repository.PatEnvironmentVariable)))
            {
                throw new BridgeConfigurationException(
                    $"Repository '{repository.Repository}' requires a valid patEnvironmentVariable.");
            }

            var localPath = ValidateDirectory(repository.LocalPath, $"{repository.Repository}.localPath");
            if (string.Equals(localPath, worktreeRoot, StringComparison.OrdinalIgnoreCase) ||
                IsDescendant(localPath, worktreeRoot) ||
                IsDescendant(worktreeRoot, localPath))
            {
                throw new BridgeConfigurationException(
                    $"worktreeRoot and local repository '{repository.Repository}' must not overlap.");
            }

            var identity = $"{server.AbsoluteUri.TrimEnd('/')}|{repository.Collection}|{repository.Project}|{repository.Repository}";
            if (!identities.Add(identity))
            {
                throw new BridgeConfigurationException($"Repository mapping '{repository.Repository}' is duplicated.");
            }
        }
    }

    private static string ValidateDirectory(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new BridgeConfigurationException($"{name} must be an absolute path.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static bool IsDescendant(string root, string candidate)
    {
        var rootPrefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^chrome-extension://[a-p]{32}/$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionOriginPattern();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+)*(?:-preview(?:\\.[0-9]+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiVersionPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariablePattern();
}
