using System.Text.RegularExpressions;

namespace DevOpsReview.Bridge.Orchestration;

public static partial class ReviewOutputSanitizer
{
    public static string Sanitize(string answer, string worktreePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreePath));
        var sanitized = answer
            .Replace($"{root}{Path.DirectorySeparatorChar}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"{root.Replace('\\', '/')}/", string.Empty, StringComparison.OrdinalIgnoreCase);

        return LocalFileLink().Replace(
            sanitized,
            static match => match.Groups["label"].Value);
    }

    [GeneratedRegex(
        @"\[(?<label>[^\]\r\n]+)\]\((?<target>(?:[A-Za-z]:[/\\]|\\\\)[^)\r\n]+|[^:/\s][^)\r\n]*:\d+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex LocalFileLink();
}
