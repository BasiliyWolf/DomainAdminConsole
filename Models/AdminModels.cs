namespace DomainAdminConsole.Models;

public sealed class DomainComputer
{
    public string Name { get; set; } = "";
    public string DnsHostName { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Description { get; set; } = "";
    public string DistinguishedName { get; set; } = "";
    public DateTime? LastLogon { get; set; }
    public bool Enabled { get; set; }
    public string IpAddress { get; set; } = "";
    public bool PingOnline { get; set; }
    public bool WinRmAvailable { get; set; }
    public bool SmbAvailable { get; set; }
    public bool RdpAvailable { get; set; }
    public string Users { get; set; } = "";
    public bool ProbeCompleted { get; set; }
    public bool IsActive => PingOnline || WinRmAvailable || SmbAvailable || RdpAvailable;
    // Compact indicators for the domain computer list.
    // Status and RDP are rendered as coloured dots by MainForm.CellFormatting.
    public string StatusDot => "●";
    public string RdpDot => "●";

    // Show only the services that are actually reachable.  This keeps the left
    // pane compact: e.g. "Ping, WinRM, SMB" or "WinRM, SMB".
    public string ConnectivityText
    {
        get
        {
            if (!ProbeCompleted) return "Проверка…";
            var available = new List<string>(3);
            if (PingOnline) available.Add("Ping");
            if (WinRmAvailable) available.Add("WinRM");
            if (SmbAvailable) available.Add("SMB");
            return available.Count == 0 ? "—" : string.Join(", ", available);
        }
    }
}

public sealed class DiskInfo
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public double SizeGb { get; set; }
    public double FreeGb { get; set; }
    public double FreePercent { get; set; }
}

public sealed class ProcessInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Cpu { get; set; }
    public double MemoryMb { get; set; }
    public string UserName { get; set; } = "";
}

public sealed class ServiceInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartType { get; set; } = "";
}

public sealed class PortInfo
{
    public string Protocol { get; set; } = "";
    public string LocalAddress { get; set; } = "";
    public int LocalPort { get; set; }
    public string Description { get; set; } = "";
    public string Process { get; set; } = "";
    public int Pid { get; set; }
}

public sealed class EventRow
{
    public DateTime? TimeCreated { get; set; }
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class SessionInfo
{
    public string Computer { get; set; } = "";
    public string User { get; set; } = "";
    public string Session { get; set; } = "";
    public string State { get; set; } = "";
    public string Raw { get; set; } = "";
}
