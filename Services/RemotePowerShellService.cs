using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DomainAdminConsole.Services;

public sealed class RemotePowerShellService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Runspace? _runspace;
    public string? ConnectedHost { get; private set; }
    public bool IsConnected => _runspace?.RunspaceStateInfo.State == RunspaceState.Opened;

    private static readonly (bool Ssl, int Port)[] WinRmEndpoints =
    [
        (false, 5985),
        (true, 5986)
    ];

    public async Task ConnectAsync(string host, PSCredential? credential = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DisconnectInternal();

            var endpoints = await GetReachableEndpointsAsync(host, cancellationToken);
            if (endpoints.Count == 0)
            {
                throw new InvalidOperationException(
                    $"На {host} не доступен WinRM: TCP 5985 (HTTP) и 5986 (HTTPS) не отвечают. " +
                    "Проверьте WinRM listener, Windows Firewall и разрешение DNS-имени.");
            }

            var errors = new List<string>();
            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = CreateConnectionInfo(
                    host,
                    endpoint.Ssl,
                    endpoint.Port,
                    openTimeout: 15000,
                    operationTimeout: 180000,
                    credential);

                var candidate = RunspaceFactory.CreateRunspace(info);
                try
                {
                    // OpenTimeout WSManConnectionInfo ограничивает само открытие сессии.
                    // CancellationToken здесь используется только для отмены по команде пользователя,
                    // а не как внутренний таймаут WinRM.
                    await Task.Run(candidate.Open, cancellationToken);

                    _runspace = candidate;
                    ConnectedHost = host;
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    try { candidate.Dispose(); } catch { }
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{EndpointName(endpoint)}: {FlattenException(ex)}");
                    try { candidate.Dispose(); } catch { }
                }
            }

            throw new InvalidOperationException(
                $"Не удалось подключиться к WinRM на {host}.\r\n" + string.Join("\r\n", errors));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ExecuteTextAsync(string script, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            using var ps = PowerShell.Create();
            ps.Runspace = _runspace;
            ps.AddScript(script).AddCommand("Out-String").AddParameter("Width", 240);
            var result = await Task.Run(() => ps.Invoke(), cancellationToken);

            var sb = new StringBuilder();
            foreach (var item in result)
                sb.Append(item?.ToString());

            foreach (var err in ps.Streams.Error)
                sb.AppendLine($"ERROR: {err}");

            return sb.ToString();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T?> ExecuteJsonAsync<T>(string script, CancellationToken cancellationToken = default)
    {
        var json = await ExecuteTextAsync($"& {{ {script} }} | ConvertTo-Json -Depth 6 -Compress", cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonSerializer.Deserialize<T>(json.Trim(), JsonOptions);
    }

    public async Task<List<T>> ExecuteJsonListAsync<T>(string script, CancellationToken cancellationToken = default)
    {
        var json = await ExecuteTextAsync($"@(& {{ {script} }}) | ConvertTo-Json -Depth 6 -Compress", cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];

        var trimmed = json.Trim();
        if (trimmed.StartsWith("["))
            return JsonSerializer.Deserialize<List<T>>(trimmed, JsonOptions) ?? [];

        var one = JsonSerializer.Deserialize<T>(trimmed, JsonOptions);
        return one is null ? [] : [one];
    }

    public static async Task<string> ExecuteOneShotTextAsync(
        string host,
        string script,
        int timeoutMs = 8000,
        PSCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        // В предыдущей версии использовался CancelAfter(timeoutMs) для общего CTS.
        // Если HTTP/5985 занимал почти весь таймаут, перед попыткой HTTPS возникал
        // OperationCanceledException. Теперь таймауты задаются штатно через WSManConnectionInfo.
        var endpoints = await GetReachableEndpointsAsync(host, cancellationToken);
        if (endpoints.Count == 0)
        {
            throw new InvalidOperationException(
                $"На {host} не доступен WinRM: TCP 5985 (HTTP) и 5986 (HTTPS) не отвечают.");
        }

        var openTimeout = Math.Clamp(timeoutMs, 7000, 15000);
        var operationTimeout = Math.Max(timeoutMs, 30000);
        var errors = new List<string>();

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = CreateConnectionInfo(
                    host,
                    endpoint.Ssl,
                    endpoint.Port,
                    openTimeout,
                    operationTimeout,
                    credential);

                using var runspace = RunspaceFactory.CreateRunspace(info);
                await Task.Run(runspace.Open, cancellationToken);

                using var ps = PowerShell.Create();
                ps.Runspace = runspace;
                ps.AddScript(script).AddCommand("Out-String").AddParameter("Width", 240);

                var output = await Task.Run(() => ps.Invoke(), cancellationToken);
                var sb = new StringBuilder();
                foreach (var item in output)
                    sb.Append(item?.ToString());
                foreach (var error in ps.Streams.Error)
                    sb.AppendLine($"ERROR: {error}");

                return sb.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{EndpointName(endpoint)}: {FlattenException(ex)}");
            }
        }

        throw new InvalidOperationException(
            $"Не удалось выполнить WinRM-команду на {host}.\r\n" + string.Join("\r\n", errors));
    }

    private static WSManConnectionInfo CreateConnectionInfo(
        string host,
        bool useSsl,
        int port,
        int openTimeout,
        int operationTimeout,
        PSCredential? credential)
    {
        var scheme = useSsl ? "https" : "http";
        var uri = new Uri($"{scheme}://{host}:{port}/wsman");

        var info = credential is null
            ? new WSManConnectionInfo(uri)
            : new WSManConnectionInfo(
                uri,
                "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                credential);

        // Ключевой hotfix 0.3.1:
        // при работе от текущей доменной учётной записи необходимо разрешить
        // неявные Windows credentials. Обычный Negotiate этого явно не включает.
        info.AuthenticationMechanism = credential is null
            ? AuthenticationMechanism.NegotiateWithImplicitCredential
            : AuthenticationMechanism.Negotiate;

        info.OpenTimeout = openTimeout;
        info.OperationTimeout = operationTimeout;
        info.CancelTimeout = 3000;
        return info;
    }

    private static async Task<List<(bool Ssl, int Port)>> GetReachableEndpointsAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var probes = WinRmEndpoints
            .Select(async endpoint => (Endpoint: endpoint, Open: await IsTcpPortOpenAsync(host, endpoint.Port, 1200, cancellationToken)))
            .ToArray();

        var results = await Task.WhenAll(probes);
        return results.Where(x => x.Open).Select(x => x.Endpoint).ToList();
    }

    private static async Task<bool> IsTcpPortOpenAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutMs);

            if (!cancellationToken.CanBeCanceled)
            {
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed != connectTask) return false;
                await connectTask;
                return tcp.Connected;
            }

            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var winner = await Task.WhenAny(connectTask, timeoutTask, cancelTask);
            if (winner == cancelTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            if (winner != connectTask) return false;
            await connectTask;
            return tcp.Connected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string EndpointName((bool Ssl, int Port) endpoint)
        => endpoint.Ssl ? $"HTTPS/{endpoint.Port}" : $"HTTP/{endpoint.Port}";

    private static string FlattenException(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) &&
                !messages.Contains(current.Message, StringComparer.OrdinalIgnoreCase))
            {
                messages.Add(current.Message.Trim());
            }
            current = current.InnerException;
        }

        return messages.Count == 0 ? ex.GetType().Name : string.Join(" -> ", messages);
    }

    public void Disconnect()
    {
        _gate.Wait();
        try { DisconnectInternal(); }
        finally { _gate.Release(); }
    }

    private void DisconnectInternal()
    {
        if (_runspace is not null)
        {
            try { _runspace.Close(); } catch { }
            _runspace.Dispose();
            _runspace = null;
        }
        ConnectedHost = null;
    }

    private void EnsureConnected()
    {
        if (!IsConnected || _runspace is null)
            throw new InvalidOperationException("Нет активного подключения к удаленному PowerShell.");
    }

    public void Dispose()
    {
        Disconnect();
        _gate.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
