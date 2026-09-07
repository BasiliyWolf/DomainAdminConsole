using DomainAdminConsole.Models;
using System.DirectoryServices;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DomainAdminConsole.Services;

public sealed class DomainService
{
    private const string NormalizedUsersScript = @"
$map=@{}
function Add-DacUser([string]$value){
 if([string]::IsNullOrWhiteSpace($value)){ return }
 $value=$value.Trim()
 $short=($value -split '\\')[-1].Trim()
 if([string]::IsNullOrWhiteSpace($short)){ return }
 $key=$short.ToLowerInvariant()
 if((-not $map.ContainsKey($key)) -or (($value -like '*\*') -and ($map[$key] -notlike '*\*'))){ $map[$key]=$value }
}
$console=(Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName
Add-DacUser $console
$q=& quser 2>$null
if($q){
 $q | Select-Object -Skip 1 | ForEach-Object {
  $line=($_ -replace '^>','').Trim()
  if($line){ $parts=$line -split '\s+'; if($parts.Count -gt 0){ Add-DacUser $parts[0] } }
 }
}
($map.Values | Sort-Object) -join ', '
";

    public string GetCurrentDomainName()
    {
        using var root = new DirectoryEntry("LDAP://RootDSE");
        var namingContext = Convert.ToString(root.Properties["defaultNamingContext"].Value) ?? "";
        return string.Join('.', namingContext
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[3..]));
    }

    public Task<List<DomainComputer>> GetDomainComputersAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
            var baseDn = Convert.ToString(rootDse.Properties["defaultNamingContext"].Value)
                         ?? throw new InvalidOperationException("Не удалось определить defaultNamingContext Active Directory.");
            using var root = new DirectoryEntry($"LDAP://{baseDn}");
            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=computer)(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000,
                SearchScope = SearchScope.Subtree
            };

            foreach (var p in new[] { "name", "dNSHostName", "operatingSystem", "description", "distinguishedName", "lastLogonTimestamp", "userAccountControl" })
                searcher.PropertiesToLoad.Add(p);

            var list = new List<DomainComputer>();
            using var results = searcher.FindAll();
            foreach (SearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(new DomainComputer
                {
                    Name = GetString(result, "name"),
                    DnsHostName = GetString(result, "dNSHostName"),
                    OperatingSystem = GetString(result, "operatingSystem"),
                    Description = GetString(result, "description"),
                    DistinguishedName = GetString(result, "distinguishedName"),
                    LastLogon = GetFileTime(result, "lastLogonTimestamp"),
                    Enabled = true
                });
            }

            return list.OrderBy(x => x.Name).ToList();
        }, cancellationToken);
    }

    public async Task ProbeComputersAsync(
        IEnumerable<DomainComputer> computers,
        int maxConcurrency = 32,
        IProgress<DomainComputer>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = computers.Select(async computer =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var host = string.IsNullOrWhiteSpace(computer.DnsHostName) ? computer.Name : computer.DnsHostName;
                computer.PingOnline = await IsPingAliveAsync(host, 700, cancellationToken);

                var winRmHttpTask = IsTcpOpenAsync(host, 5985, 900, cancellationToken);
                var winRmHttpsTask = IsTcpOpenAsync(host, 5986, 900, cancellationToken);
                var smbTask = IsTcpOpenAsync(host, 445, 900, cancellationToken);
                var rdpTask = IsTcpOpenAsync(host, 3389, 900, cancellationToken);
                await Task.WhenAll(winRmHttpTask, winRmHttpsTask, smbTask, rdpTask);

                var winRmHttp = winRmHttpTask.Result;
                computer.WinRmAvailable = winRmHttp || winRmHttpsTask.Result;
                computer.SmbAvailable = smbTask.Result;
                computer.RdpAvailable = rdpTask.Result;

                if (computer.IsActive)
                    computer.IpAddress = await ResolveIpv4Async(host, cancellationToken);

                if (computer.WinRmAvailable)
                {
                    try
                    {
                        computer.Users = await GetLoggedOnUsersTextAsync(host, cancellationToken);
                    }
                    catch { }
                }
                computer.ProbeCompleted = true;
                progress?.Report(computer);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                computer.ProbeCompleted = true;
                progress?.Report(computer);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task<List<SessionInfo>> FindUserAcrossDomainAsync(
        IEnumerable<DomainComputer> computers,
        string userSearch,
        int maxConcurrency = 20,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var needle = userSearch.Trim();
        if (needle.Contains('\\')) needle = needle[(needle.LastIndexOf('\\') + 1)..];

        var found = new System.Collections.Concurrent.ConcurrentBag<SessionInfo>();
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        int done = 0;
        var all = computers.Where(c => c.Enabled).ToList();

        var tasks = all.Select(async computer =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var host = string.IsNullOrWhiteSpace(computer.DnsHostName) ? computer.Name : computer.DnsHostName;
                var matched = false;
                if (computer.WinRmAvailable || await IsTcpOpenAsync(host, 5985, 600, cancellationToken))
                {
                    try
                    {
                        var users = await GetLoggedOnUsersTextAsync(host, cancellationToken);
                        if (users.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found.Add(new SessionInfo
                            {
                                Computer = computer.Name,
                                User = users,
                                Session = "Console/RDP",
                                State = "Найден (WinRM)",
                                Raw = users
                            });
                            matched = true;
                        }
                    }
                    catch { }
                }

                if (!matched && computer.RdpAvailable)
                {
                    try
                    {
                        var sessions = await RdpSessionService.GetSessionsAsync(host, cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                        foreach (var session in sessions.Where(x => x.UserName.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.UserDisplay.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                        {
                            found.Add(new SessionInfo
                            {
                                Computer = computer.Name,
                                User = session.UserDisplay,
                                Session = $"{session.StationName} / ID {session.Id}",
                                State = session.State,
                                Raw = $"WTS session {session.Id}"
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                var current = Interlocked.Increment(ref done);
                status?.Report($"Проверено {current} из {all.Count}");
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return found.OrderBy(x => x.Computer).ToList();
    }

    public async Task<string> GetLoggedOnUsersTextAsync(string host, CancellationToken cancellationToken = default)
    {
        var raw = (await RemotePowerShellService.ExecuteOneShotTextAsync(
            host, NormalizedUsersScript, 9000, null, cancellationToken)).Trim();
        return NormalizeUsers(raw);
    }

    private static string NormalizeUsers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in raw.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = token.Trim().TrimStart('>');
            if (string.IsNullOrWhiteSpace(value)) continue;

            var slash = value.LastIndexOf('\\');
            var shortName = slash >= 0 && slash + 1 < value.Length ? value[(slash + 1)..] : value;
            shortName = shortName.Trim();
            if (string.IsNullOrWhiteSpace(shortName)) continue;

            if (!users.TryGetValue(shortName, out var current) ||
                (value.Contains('\\') && !current.Contains('\\')))
            {
                users[shortName] = value;
            }
        }

        return string.Join(", ", users.Values.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
    }

    public async Task<List<SessionInfo>> GetUsersOnComputerAsync(string host, CancellationToken cancellationToken = default)
    {
        var result = new List<SessionInfo>();
        try
        {
            var sessions = await RdpSessionService.GetSessionsAsync(host, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            foreach (var session in sessions
                         .GroupBy(x => x.UserDisplay, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.First()))
            {
                result.Add(new SessionInfo
                {
                    Computer = host,
                    User = session.UserDisplay,
                    Session = $"{session.StationName} / ID {session.Id}",
                    State = session.State,
                    Raw = $"WTS session {session.Id}"
                });
            }
        }
        catch { }

        try
        {
            var users = await GetLoggedOnUsersTextAsync(host, cancellationToken);
            foreach (var user in users.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var shortName = user.Contains('\\') ? user[(user.LastIndexOf('\\') + 1)..] : user;
                if (result.Any(x =>
                    x.User.Equals(user, StringComparison.OrdinalIgnoreCase) ||
                    x.User.EndsWith("\\" + shortName, StringComparison.OrdinalIgnoreCase) ||
                    x.User.Equals(shortName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Add(new SessionInfo
                {
                    Computer = host,
                    User = user,
                    Session = "Console",
                    State = "Active",
                    Raw = "WinRM console user"
                });
            }
        }
        catch when (result.Count > 0) { }

        if (result.Count == 0)
            throw new InvalidOperationException($"Не удалось получить пользовательские сеансы на {host}.");

        return result.OrderBy(x => x.User, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Session).ToList();
    }

    private static async Task<bool> IsPingAliveAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + 250), ct);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    private static async Task<bool> IsTcpOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            return tcp.Connected;
        }
        catch { return false; }
    }

    private static async Task<string> ResolveIpv4Async(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static string GetString(SearchResult r, string name)
        => r.Properties.Contains(name) && r.Properties[name].Count > 0
            ? Convert.ToString(r.Properties[name][0]) ?? ""
            : "";

    private static DateTime? GetFileTime(SearchResult r, string name)
    {
        try
        {
            if (!r.Properties.Contains(name) || r.Properties[name].Count == 0) return null;
            var value = Convert.ToInt64(r.Properties[name][0]);
            return value <= 0 ? null : DateTime.FromFileTimeUtc(value).ToLocalTime();
        }
        catch { return null; }
    }
}
