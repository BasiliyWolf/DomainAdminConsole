namespace DomainAdminConsole.Models;

public sealed class FileManagerEntry
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? LastWriteTime { get; set; }
    public string Type => IsDirectory ? "Папка" : "Файл";
    public double SizeMb => IsDirectory ? 0 : Math.Round(SizeBytes / 1024d / 1024d, 2);
}

public sealed class BulkTarget
{
    public bool Selected { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Users { get; set; } = "";
    public bool WinRmAvailable { get; set; }
}

public sealed class BulkResult
{
    public DateTime Time { get; set; }
    public string Host { get; set; } = "";
    public string Action { get; set; } = "";
    public bool Success { get; set; }
    public string Result { get; set; } = "";
}

public sealed class BitLockerVolumeInfo
{
    public string MountPoint { get; set; } = "";
    public string VolumeType { get; set; } = "";
    public string VolumeStatus { get; set; } = "";
    public string ProtectionStatus { get; set; } = "";
    public string LockStatus { get; set; } = "";
    public string EncryptionMethod { get; set; } = "";
    public double EncryptionPercentage { get; set; }
    public string KeyProtectors { get; set; } = "";
}

public sealed class TpmInfo
{
    public bool TpmPresent { get; set; }
    public bool TpmReady { get; set; }
    public bool TpmEnabled { get; set; }
    public bool TpmActivated { get; set; }
    public bool TpmOwned { get; set; }
    public bool RestartPending { get; set; }
    public string ManufacturerIdTxt { get; set; } = "";
    public string ManufacturerVersion { get; set; } = "";
    public string ManagedAuthLevel { get; set; } = "";
    public string SpecVersion { get; set; } = "";
}

public sealed class PrinterInfoRow
{
    public string Name { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string PortName { get; set; } = "";
    public bool Shared { get; set; }
    public string ShareName { get; set; } = "";
    public string Type { get; set; } = "";
    public string PrinterStatus { get; set; } = "";
}

public sealed class PrintJobInfoRow
{
    public int Id { get; set; }
    public string PrinterName { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string JobStatus { get; set; } = "";
    public int PagesPrinted { get; set; }
    public int TotalPages { get; set; }
    public long Size { get; set; }
}

public sealed class CertificateInfoRow
{
    public string Store { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public bool HasPrivateKey { get; set; }
    public string FriendlyName { get; set; } = "";
    public string DnsNames { get; set; } = "";
}

public sealed class FirewallProfileInfoRow
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string DefaultInboundAction { get; set; } = "";
    public string DefaultOutboundAction { get; set; } = "";
    public bool NotifyOnListen { get; set; }
    public bool LogAllowed { get; set; }
    public bool LogBlocked { get; set; }
}

public sealed class FirewallRuleInfoRow
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Enabled { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Action { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string LocalPort { get; set; } = "";
    public string RemotePort { get; set; } = "";
    public string Program { get; set; } = "";
    public string Service { get; set; } = "";
}

public sealed class SmbSessionInfoRow
{
    public long SessionId { get; set; }
    public string ClientComputerName { get; set; } = "";
    public string ClientUserName { get; set; } = "";
    public int NumOpens { get; set; }
    public long SecondsExists { get; set; }
    public long SecondsIdle { get; set; }
    public string Dialect { get; set; } = "";
    public bool Encrypted { get; set; }
}

public sealed class SmbOpenFileInfoRow
{
    public long FileId { get; set; }
    public long SessionId { get; set; }
    public string ClientComputerName { get; set; } = "";
    public string ClientUserName { get; set; } = "";
    public string Path { get; set; } = "";
    public string ShareRelativePath { get; set; } = "";
    public int Locks { get; set; }
}

public sealed class DeviceInfoRow
{
    public string Status { get; set; } = "";
    public string Class { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string DriverProviderName { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public string DriverDate { get; set; } = "";
    public string InfName { get; set; } = "";
}

public sealed class SavedPowerShellScript
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Общие";
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class KnownMacInfo
{
    public string Host { get; set; } = "";
    public string InterfaceAlias { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTime LastSeen { get; set; } = DateTime.Now;
}
