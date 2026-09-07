using DomainAdminConsole.Models;
using System.Text.Json;

namespace DomainAdminConsole.Services;

public sealed class AppDataStore
{
    private readonly string _basePath;
    private readonly string _favoritesPath;
    private readonly string _auditPath;
    private readonly string _scriptsPath;
    private readonly string _knownMacsPath;
    private readonly object _auditLock = new();

    public AppDataStore()
    {
        _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DomainAdminConsole");
        _favoritesPath = Path.Combine(_basePath, "favorites.json");
        _auditPath = Path.Combine(_basePath, "audit.jsonl");
        _scriptsPath = Path.Combine(_basePath, "scripts.json");
        _knownMacsPath = Path.Combine(_basePath, "known_macs.json");
        Directory.CreateDirectory(_basePath);
    }

    public string DataDirectory => _basePath;
    public string AuditPath => _auditPath;

    public List<FavoriteComputer> LoadFavorites()
    {
        try
        {
            if (!File.Exists(_favoritesPath)) return [];
            var json = File.ReadAllText(_favoritesPath);
            return JsonSerializer.Deserialize<List<FavoriteComputer>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveFavorites(IEnumerable<FavoriteComputer> favorites)
    {
        Directory.CreateDirectory(_basePath);
        var json = JsonSerializer.Serialize(favorites.OrderBy(x => x.Group).ThenBy(x => x.Name), JsonOptions);
        File.WriteAllText(_favoritesPath, json);
    }


    public List<SavedPowerShellScript> LoadScripts()
    {
        try
        {
            if (!File.Exists(_scriptsPath)) return [];
            return JsonSerializer.Deserialize<List<SavedPowerShellScript>>(File.ReadAllText(_scriptsPath), JsonOptions) ?? [];
        }
        catch { return []; }
    }

    public void SaveScripts(IEnumerable<SavedPowerShellScript> scripts)
    {
        Directory.CreateDirectory(_basePath);
        File.WriteAllText(_scriptsPath, JsonSerializer.Serialize(scripts.OrderBy(x => x.Category).ThenBy(x => x.Name), JsonOptions));
    }

    public List<KnownMacInfo> LoadKnownMacs()
    {
        try
        {
            if (!File.Exists(_knownMacsPath)) return [];
            return JsonSerializer.Deserialize<List<KnownMacInfo>>(File.ReadAllText(_knownMacsPath), JsonOptions) ?? [];
        }
        catch { return []; }
    }

    public void MergeKnownMacs(IEnumerable<KnownMacInfo> items)
    {
        var all = LoadKnownMacs();
        foreach (var item in items)
        {
            var existing = all.FirstOrDefault(x => x.Host.Equals(item.Host, StringComparison.OrdinalIgnoreCase)
                && x.InterfaceAlias.Equals(item.InterfaceAlias, StringComparison.OrdinalIgnoreCase));
            if (existing is null) all.Add(item);
            else
            {
                existing.MacAddress = item.MacAddress;
                existing.IpAddress = item.IpAddress;
                existing.LastSeen = item.LastSeen;
            }
        }
        Directory.CreateDirectory(_basePath);
        File.WriteAllText(_knownMacsPath, JsonSerializer.Serialize(all.OrderByDescending(x => x.LastSeen), JsonOptions));
    }

    public void AppendAudit(AuditEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_basePath);
            var json = JsonSerializer.Serialize(entry, AuditJsonOptions);
            lock (_auditLock)
                File.AppendAllText(_auditPath, json + Environment.NewLine);
        }
        catch
        {
            // Audit logging must never break the administrator workflow.
        }
    }

    public List<AuditEntry> ReadAudit(int maxEntries = 2000)
    {
        try
        {
            if (!File.Exists(_auditPath)) return [];
            return File.ReadLines(_auditPath)
                .Reverse()
                .Take(maxEntries)
                .Select(line =>
                {
                    try { return JsonSerializer.Deserialize<AuditEntry>(line, AuditJsonOptions); }
                    catch { return null; }
                })
                .Where(x => x is not null)
                .Cast<AuditEntry>()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
