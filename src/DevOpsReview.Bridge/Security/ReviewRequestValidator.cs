using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Protocol;

namespace DevOpsReview.Bridge.Security;

public sealed class ReviewRequestValidator(BridgeOptions options)
{
    private const int MaxQuestionLength = 4_000;
    private const int MaxSelectedTextLength = 32_000;
    private const int MaxSelectionLines = 1_000;
    private const int MaxNameLength = 256;

    public ValidatedReviewRequest Validate(ReviewStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var serverUri = ValidateServerUri(request.ServerUrl);
        var collection = ValidateName(request.Collection, nameof(request.Collection));
        var project = ValidateName(request.Project, nameof(request.Project));
        var repository = ValidateName(request.Repository, nameof(request.Repository));

        if (request.PullRequestId <= 0)
        {
            throw new ReviewRequestValidationException("invalid_pull_request", "Pull request ID must be positive.");
        }

        var filePath = NormalizeRepositoryPath(request.FilePath);
        ValidateLines(request.StartLine, request.EndLine);

        var question = request.Question.Trim();
        if (question.Length is 0 or > MaxQuestionLength)
        {
            throw new ReviewRequestValidationException(
                "invalid_question",
                $"Question length must be between 1 and {MaxQuestionLength} characters.");
        }

        if (request.SelectedText.Length > MaxSelectedTextLength)
        {
            throw new ReviewRequestValidationException(
                "selection_too_large",
                $"Selected text must not exceed {MaxSelectedTextLength} characters.");
        }

        var mapping = ResolveMapping(serverUri, collection, project, repository);

        return new ValidatedReviewRequest(
            serverUri,
            collection,
            project,
            repository,
            request.PullRequestId,
            filePath,
            request.StartLine,
            request.EndLine,
            request.SelectedText,
            question,
            mapping);
    }

    private static Uri ValidateServerUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ReviewRequestValidationException(
                "invalid_server_url",
                "Azure DevOps Server URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");
        }

        return NormalizeServerUri(uri);
    }

    private static string ValidateName(string value, string fieldName)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxNameLength || trimmed.Any(char.IsControl))
        {
            throw new ReviewRequestValidationException(
                "invalid_repository_locator",
                $"{fieldName} is empty, too long, or contains control characters.");
        }

        return trimmed;
    }

    private static string NormalizeRepositoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_048 || value.Contains('\0'))
        {
            throw new ReviewRequestValidationException("invalid_file_path", "File path is empty or invalid.");
        }

        if (value.Contains('\\') || Path.IsPathFullyQualified(value))
        {
            throw new ReviewRequestValidationException(
                "invalid_file_path",
                "File path must use repository-relative forward slashes.");
        }

        var normalized = value[0] == '/' ? value : $"/{value}";
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new ReviewRequestValidationException("invalid_file_path", "File path contains an invalid segment.");
        }

        return $"/{string.Join('/', segments)}";
    }

    private static void ValidateLines(int startLine, int endLine)
    {
        if (startLine <= 0 || endLine < startLine || endLine - startLine + 1 > MaxSelectionLines)
        {
            throw new ReviewRequestValidationException(
                "invalid_line_range",
                $"Line range must be positive, ordered, and contain at most {MaxSelectionLines} lines.");
        }
    }

    private RepositoryMapping ResolveMapping(Uri serverUri, string collection, string project, string repository)
    {
        var mapping = options.Repositories.SingleOrDefault(candidate =>
            NormalizeServerUri(ParseConfiguredServer(candidate.ServerUrl)) == serverUri &&
            string.Equals(candidate.Collection, collection, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Project, project, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Repository, repository, StringComparison.OrdinalIgnoreCase));

        return mapping ?? throw new ReviewRequestValidationException(
            "repository_not_allowed",
            "The requested Azure DevOps repository is not configured for local review.");
    }

    private static Uri ParseConfiguredServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new BridgeConfigurationException($"Configured server URL '{value}' is invalid.");
        }

        return uri;
    }

    private static Uri NormalizeServerUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri;
    }
}

public sealed record ValidatedReviewRequest(
    Uri ServerUri,
    string Collection,
    string Project,
    string Repository,
    int PullRequestId,
    string FilePath,
    int StartLine,
    int EndLine,
    string SelectedText,
    string Question,
    RepositoryMapping Mapping);

public sealed class ReviewRequestValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
