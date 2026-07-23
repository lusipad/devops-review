namespace DevOpsReview.Bridge.Configuration;

public sealed record AzureDevOpsRepositoryLocator(
    string ServerUrl,
    string Collection,
    string Project,
    string Repository)
{
    public static AzureDevOpsRepositoryLocator Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new FormatException("请输入完整的 HTTP 或 HTTPS Azure DevOps 地址。");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var gitIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "_git", StringComparison.OrdinalIgnoreCase));
        if (gitIndex < 2 || gitIndex + 1 >= segments.Length)
        {
            throw new FormatException(
                "地址必须包含 /<Collection>/<Project>/_git/<Repository>。");
        }

        var serverPath = string.Join('/', segments[..(gitIndex - 2)]);
        var serverUrl = uri.GetLeftPart(UriPartial.Authority);
        if (serverPath.Length > 0)
        {
            serverUrl += $"/{serverPath}";
        }

        return new AzureDevOpsRepositoryLocator(
            serverUrl,
            Decode(segments[gitIndex - 2]),
            Decode(segments[gitIndex - 1]),
            Decode(segments[gitIndex + 1]));
    }

    public string ToRepositoryUrl() =>
        $"{ServerUrl.TrimEnd('/')}/{Uri.EscapeDataString(Collection)}/{Uri.EscapeDataString(Project)}/_git/{Uri.EscapeDataString(Repository)}";

    public bool IdentifiesSameRepository(AzureDevOpsRepositoryLocator other) =>
        string.Equals(
            ServerUrl.TrimEnd('/'),
            other.ServerUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Collection, other.Collection, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Project, other.Project, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Repository, other.Repository, StringComparison.OrdinalIgnoreCase);

    private static string Decode(string value) => Uri.UnescapeDataString(value);
}
