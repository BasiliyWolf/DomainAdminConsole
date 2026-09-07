namespace DomainAdminConsole.Models;

public sealed class FavoriteComputer
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string Group { get; set; } = "Общие";
    public string Notes { get; set; } = "";
}

public sealed class ScheduledTaskInfo
{
    public string TaskName { get; set; } = "";
    public string TaskPath { get; set; } = "";
    public string State { get; set; } = "";
    public string Author { get; set; } = "";
}

public sealed class InstalledAppInfo
{
    public string DisplayName { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string InstallDate { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string UninstallString { get; set; } = "";
}

public sealed class LocalAccountInfo
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string LastLogon { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAdministrator { get; set; }
}


public sealed class LocalGroupMemberInfo
{
    public string Name { get; set; } = "";
    public string ObjectClass { get; set; } = "";
    public string AdsPath { get; set; } = "";
}

public sealed class NetworkAdapterInfo
{
    public string InterfaceAlias { get; set; } = "";
    public string Status { get; set; } = "";
    public string IPv4Address { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string DnsServers { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public int InterfaceIndex { get; set; }
}

public sealed class RouteInfo
{
    public string DestinationPrefix { get; set; } = "";
    public string NextHop { get; set; } = "";
    public string InterfaceAlias { get; set; } = "";
    public int RouteMetric { get; set; }
}

public sealed class HotFixInfo
{
    public string HotFixId { get; set; } = "";
    public string Description { get; set; } = "";
    public string InstalledOn { get; set; } = "";
    public string InstalledBy { get; set; } = "";
}

public sealed class PendingUpdateInfo
{
    public string Title { get; set; } = "";
    public string Kb { get; set; } = "";
    public bool IsDownloaded { get; set; }
    public bool RebootRequired { get; set; }
}

public sealed class AuditEntry
{
    public DateTime Time { get; set; }
    public string Host { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public bool Success { get; set; }
}

public sealed class RdpSessionInfo
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string Domain { get; set; } = "";
    public string StationName { get; set; } = "";
    public string State { get; set; } = "";
    public string UserDisplay => string.IsNullOrWhiteSpace(Domain) ? UserName : $"{Domain}\\{UserName}";
}
