using NetBypass.Core.Models;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class DiagnosticRetryPolicyTests
{
    [Theory]
    [InlineData("SocketException")]
    [InlineData("IOException")]
    [InlineData("OperationCanceledException")]
    public void ShouldRetry_TransientNetworkFailure(string errorCode)
    {
        var result = Result(false, errorCode);

        Assert.True(DiagnosticRetryPolicy.ShouldRetry(result, 1, 3));
    }

    [Fact]
    public void ShouldRetry_DoesNotRetryPermanentTlsFailure()
    {
        var result = Result(false, "AuthenticationException");

        Assert.False(DiagnosticRetryPolicy.ShouldRetry(result, 1, 3));
    }

    [Fact]
    public void ShouldRetry_StopsAfterMaximumAttempts()
    {
        var result = Result(false, "OperationCanceledException");

        Assert.False(DiagnosticRetryPolicy.ShouldRetry(result, 3, 3));
    }

    [Fact]
    public void DelayBeforeAttempt_UsesExponentialBackoff()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500),
            DiagnosticRetryPolicy.DelayBeforeAttempt(2));
        Assert.Equal(TimeSpan.FromSeconds(1),
            DiagnosticRetryPolicy.DelayBeforeAttempt(3));
    }

    private static ServiceDiagnosticResult Result(bool reachable, string errorCode) =>
        new(
            "demo",
            "Demo",
            "203.0.113.10",
            reachable,
            [],
            [new ProbeResult(
                ProbeStage.Tcp,
                reachable ? ProbeStatus.Success : ProbeStatus.Failed,
                TimeSpan.FromMilliseconds(10),
                "203.0.113.10",
                errorCode,
                "test",
                DateTimeOffset.UtcNow)],
            DateTimeOffset.UtcNow);
}
