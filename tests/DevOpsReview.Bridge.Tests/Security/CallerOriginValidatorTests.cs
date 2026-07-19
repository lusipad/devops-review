using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Security;

namespace DevOpsReview.Bridge.Tests.Security;

public sealed class CallerOriginValidatorTests
{
    private const string Allowed = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/";

    [Fact]
    public void ValidateAcceptsExactCallerAmongBrowserArguments()
    {
        CallerOriginValidator.Validate(
            [Allowed, "--parent-window=0"],
            [Allowed]);
    }

    [Theory]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop.evil/")]
    [InlineData("chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba/")]
    [InlineData("--parent-window=0")]
    public void ValidateRejectsOtherCallers(string caller)
    {
        Assert.Throws<BridgeConfigurationException>(() =>
            CallerOriginValidator.Validate([caller], [Allowed]));
    }
}
