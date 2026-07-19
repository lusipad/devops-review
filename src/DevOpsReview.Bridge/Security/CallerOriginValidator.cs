using DevOpsReview.Bridge.Configuration;

namespace DevOpsReview.Bridge.Security;

public static class CallerOriginValidator
{
    public static void Validate(IEnumerable<string> arguments, IReadOnlyList<string> allowedOrigins)
    {
        var origin = arguments.FirstOrDefault(argument =>
            argument.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase));
        if (origin is null || !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            throw new BridgeConfigurationException("Native messaging caller origin is not allowed.");
        }
    }
}
