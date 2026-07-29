using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public static class DiagnosticRetryPolicy
{
    private static readonly HashSet<string> TransientErrorCodes =
    [
        "SocketException",
        "IOException",
        "OperationCanceledException"
    ];

    public static bool ShouldRetry(
        ServiceDiagnosticResult result,
        int completedAttempt,
        int maximumAttempts)
    {
        if (result.IsReachable || completedAttempt >= maximumAttempts)
            return false;

        return result.Probes.Any(probe =>
            probe.Status == ProbeStatus.Failed
            && probe.ErrorCode is not null
            && TransientErrorCodes.Contains(probe.ErrorCode));
    }

    public static TimeSpan DelayBeforeAttempt(int nextAttempt) =>
        TimeSpan.FromMilliseconds(500 * Math.Pow(2, Math.Max(0, nextAttempt - 2)));
}
