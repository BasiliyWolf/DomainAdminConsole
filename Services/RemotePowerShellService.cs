using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace DomainAdminConsole.Services;

public sealed class RemotePowerShellService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Runspace? _runspace;
    public string? ConnectedHost { get; private set; }
    public bool IsConnected => _runspace?.RunspaceStateInfo.State == RunspaceState.Opened;

    public async Task ConnectAsync(string host, PSCredential? credential = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DisconnectInternal();
            Exception? lastError = null;

            foreach (var endpoint in new[] { (Ssl: false, Port: 5985), (Ssl: true, Port: 5986) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = CreateConnectionInfo(host, endpoint.Ssl, endpoint.Port, 7000, 180000, credential);
                var candidate = RunspaceFactory.CreateRunspace(info);
                try
                {
                    await Task.Run(candidate.Open, cancellationToken);
                    _runspace = candidate;
                    ConnectedHost = host;
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { candidate.Dispose(); } catch { }
                }
            }

            throw new InvalidOperationException($"Не удалось подключиться к WinRM на {host} по HTTP 5985 или HTTPS 5986.", lastError);
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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        return await Task.Run(() =>
        {
            Exception? lastError = null;
            foreach (var endpoint in new[] { (Ssl: false, Port: 5985), (Ssl: true, Port: 5986) })
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                try
                {
                    var info = CreateConnectionInfo(host, endpoint.Ssl, endpoint.Port, Math.Min(timeoutMs, 4000), timeoutMs, credential);
                    using var runspace = RunspaceFactory.CreateRunspace(info);
                    runspace.Open();
                    using var ps = PowerShell.Create();
                    ps.Runspace = runspace;
                    ps.AddScript(script).AddCommand("Out-String").AddParameter("Width", 240);
                    var output = ps.Invoke();
                    var sb = new StringBuilder();
                    foreach (var item in output) sb.Append(item?.ToString());
                    foreach (var error in ps.Streams.Error) sb.AppendLine($"ERROR: {error}");
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            throw new InvalidOperationException($"Не удалось выполнить WinRM-команду на {host}.", lastError);
        }, timeoutCts.Token);
    }

    private static WSManConnectionInfo CreateConnectionInfo(string host, bool useSsl, int port, int openTimeout, int operationTimeout, PSCredential? credential)
    {
        var scheme = useSsl ? "https" : "http";
        var uri = new Uri($"{scheme}://{host}:{port}/wsman");
        var info = credential is null
            ? new WSManConnectionInfo(uri)
            : new WSManConnectionInfo(uri, "http://schemas.microsoft.com/powershell/Microsoft.PowerShell", credential);

        info.AuthenticationMechanism = AuthenticationMechanism.Negotiate;
        info.OpenTimeout = openTimeout;
        info.OperationTimeout = operationTimeout;
        info.CancelTimeout = 3000;
        return info;
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
