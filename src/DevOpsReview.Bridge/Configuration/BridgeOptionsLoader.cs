using System.Text.Json;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Bridge.Configuration;

public static class BridgeOptionsLoader
{
    public const string ConfigEnvironmentVariable = "DEVOPS_REVIEW_CONFIG";

    public static async Task<BridgeOptions> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuredPath = Environment.GetEnvironmentVariable(ConfigEnvironmentVariable);
        var configPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevOpsReview",
                "config.json")
            : configuredPath;

        if (!File.Exists(configPath))
        {
            throw new BridgeConfigurationException(
                $"Configuration file was not found. Set {ConfigEnvironmentVariable} or create '{configPath}'.");
        }

        await using var stream = File.OpenRead(configPath);
        var options = await JsonSerializer.DeserializeAsync<BridgeOptions>(
            stream,
            BridgeJson.SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        var loaded = options ?? throw new BridgeConfigurationException("Configuration file is empty or invalid.");
        BridgeOptionsValidator.Validate(loaded);
        return loaded;
    }
}

public sealed class BridgeConfigurationException(string message) : Exception(message);
