using System.Text.Json.Serialization;

namespace DevOpsReview.Bridge.Configuration;

public sealed record BridgeOptions
{
    [JsonPropertyName("dataDirectory")]
    public string DataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsReview");

    [JsonPropertyName("worktreeRoot")]
    public string WorktreeRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsReview",
        "worktrees");

    [JsonPropertyName("codexExecutable")]
    public string CodexExecutable { get; init; } = "codex";

    [JsonPropertyName("allowedExtensionOrigins")]
    public IReadOnlyList<string> AllowedExtensionOrigins { get; init; } = [];

    [JsonPropertyName("repositories")]
    public IReadOnlyList<RepositoryMapping> Repositories { get; init; } = [];
}

public sealed record RepositoryMapping
{
    [JsonPropertyName("serverUrl")]
    public required string ServerUrl { get; init; }

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("project")]
    public required string Project { get; init; }

    [JsonPropertyName("repository")]
    public required string Repository { get; init; }

    [JsonPropertyName("localPath")]
    public required string LocalPath { get; init; }

    [JsonPropertyName("authMode")]
    public AzureDevOpsAuthMode AuthMode { get; init; } = AzureDevOpsAuthMode.Windows;

    [JsonPropertyName("patEnvironmentVariable")]
    public string? PatEnvironmentVariable { get; init; }

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "7.0";
}

[JsonConverter(typeof(JsonStringEnumConverter<AzureDevOpsAuthMode>))]
public enum AzureDevOpsAuthMode
{
    Windows,
    Pat,
}
