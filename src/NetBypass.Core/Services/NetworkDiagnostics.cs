using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public interface IDohResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken);
}

public interface IEndpointProbe
{
    Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        CancellationToken cancellationToken);
}

public sealed class CloudflareGoogleDohResolver : IDohResolver
{
    private static readonly Uri[] Endpoints =
    [
        new("https://cloudflare-dns.com/dns-query"),
        new("https://dns.google/resolve")
    ];

    private readonly HttpClient _httpClient;

    public CloudflareGoogleDohResolver(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken)
    {
        var addresses = new HashSet<IPAddress>();

        foreach (var endpoint in Endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{endpoint}?name={Uri.EscapeDataString(hostname)}&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<DohResponse>(
                    cancellationToken: cancellationToken);

                foreach (var answer in payload?.Answer ?? [])
                {
                    if (answer.Type == 1 && IPAddress.TryParse(answer.Data, out var address))
                        addresses.Add(address);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or TaskCanceledException
                    or NotSupportedException)
            {
                // A second resolver may still succeed. The caller receives an
                // empty list only when all configured resolvers fail.
            }
        }

        return addresses.ToArray();
    }

    private sealed record DohResponse(DohAnswer[]? Answer);
    private sealed record DohAnswer(int Type, string Data);
}

public sealed class EndpointProbe(TimeSpan? timeout = null) : IEndpointProbe
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(4);

    public async Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        CancellationToken cancellationToken)
    {
        var results = new List<ProbeResult>();
        var checkedAt = DateTimeOffset.UtcNow;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        using var client = new TcpClient(targetAddress.AddressFamily);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(
                targetAddress,
                healthCheck.Port,
                timeoutSource.Token);
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tcp,
                ProbeStatus.Success,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                null,
                "TCP-соединение установлено",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is SocketException
                or IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tcp,
                ProbeStatus.Failed,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                exception is OperationCanceledException
                    ? "Истекло время TCP-подключения"
                    : $"TCP недоступен: {exception.Message}",
                checkedAt));
            return results;
        }

        await using var networkStream = client.GetStream();
        using var tlsStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        stopwatch.Restart();
        try
        {
            await tlsStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = healthCheck.Host,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
                },
                timeoutSource.Token);
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tls,
                ProbeStatus.Success,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                null,
                $"TLS-сертификат для {healthCheck.Host} действителен",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is AuthenticationException
                or IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tls,
                ProbeStatus.Failed,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                exception is OperationCanceledException
                    ? "Истекло время TLS-проверки"
                    : $"TLS-проверка не пройдена: {exception.Message}",
                checkedAt));
            return results;
        }

        stopwatch.Restart();
        try
        {
            var request = Encoding.ASCII.GetBytes(
                $"HEAD / HTTP/1.1\r\nHost: {healthCheck.Host}\r\nUser-Agent: NetBypass-Diagnostics/1.0\r\nConnection: close\r\n\r\n");
            await tlsStream.WriteAsync(request, timeoutSource.Token);
            await tlsStream.FlushAsync(timeoutSource.Token);

            using var reader = new StreamReader(
                tlsStream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var statusLine = await reader.ReadLineAsync(timeoutSource.Token);
            stopwatch.Stop();
            var statusCode = ParseHttpStatusCode(statusLine);
            var accepted = statusCode.HasValue
                && healthCheck.AcceptedHttpStatuses.Contains(statusCode.Value);
            results.Add(new ProbeResult(
                ProbeStage.Http,
                accepted ? ProbeStatus.Success : ProbeStatus.Warning,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                statusCode?.ToString(),
                statusCode.HasValue
                    ? $"HTTP ответил кодом {statusCode}"
                    : "Не удалось прочитать HTTP-статус",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Http,
                ProbeStatus.Warning,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                "TCP и TLS доступны, но HTTP-проверка не завершена",
                checkedAt));
        }

        return results;
    }

    private static int? ParseHttpStatusCode(string? statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine))
            return null;

        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out var status)
            ? status
            : null;
    }
}

public sealed class NetworkDiagnosticService(
    IDohResolver dohResolver,
    IEndpointProbe endpointProbe)
{
    public async Task<ServiceDiagnosticResult> DiagnoseAsync(
        ServiceProfile profile,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var results = new List<ProbeResult>();
        var resolvedAddresses = new HashSet<IPAddress>();

        foreach (var healthCheck in profile.HealthChecks)
        {
            var resolved = await dohResolver.ResolveAsync(
                healthCheck.Host,
                cancellationToken);
            resolvedAddresses.UnionWith(resolved);
            results.Add(new ProbeResult(
                ProbeStage.Dns,
                resolved.Count > 0 ? ProbeStatus.Success : ProbeStatus.Warning,
                null,
                resolved.FirstOrDefault()?.ToString(),
                resolved.Count > 0 ? null : "DohResolutionFailed",
                resolved.Count > 0
                    ? $"{healthCheck.Host}: DoH вернул адресов — {resolved.Count}"
                    : $"{healthCheck.Host}: DoH не ответил",
                checkedAt));

            var target = IPAddress.Parse(healthCheck.TargetAddress);
            results.AddRange(await endpointProbe.ProbeAsync(
                healthCheck,
                target,
                cancellationToken));
        }

        var expectedEndpointChecks = profile.HealthChecks.Count;
        var reachable = results.Count(result =>
                result.Stage == ProbeStage.Tcp && result.Status == ProbeStatus.Success)
            == expectedEndpointChecks
            && results.Count(result =>
                result.Stage == ProbeStage.Tls && result.Status == ProbeStatus.Success)
            == expectedEndpointChecks;

        return new ServiceDiagnosticResult(
            profile.Id,
            profile.Name,
            string.Join(", ", profile.HealthChecks.Select(check => check.TargetAddress)),
            reachable,
            resolvedAddresses.Select(address => address.ToString()).ToArray(),
            results,
            checkedAt);
    }
}
