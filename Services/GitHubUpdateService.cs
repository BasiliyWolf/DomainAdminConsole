using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace DomainAdminConsole.Services;

internal sealed record UpdateCheckResult(
    bool Success,
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string? LatestTag,
    string? ReleaseName,
    Uri? ReleasePage,
    string? AssetName,
    Uri? AssetDownloadUri,
    long? AssetSize,
    string? Error)
{
    public bool CanSelfUpdate => AssetDownloadUri is not null && !string.IsNullOrWhiteSpace(AssetName);
}

internal sealed record PreparedUpdateResult(bool Success, string? UpdaterScriptPath, string? Error);

internal sealed class GitHubUpdateService
{
    public const string RepositoryOwner = "BasiliyWolf";
    public const string RepositoryName = "DomainAdminConsole";

    public static Uri RepositoryUri => new("https://github.com/BasiliyWolf/DomainAdminConsole");
    public static Uri LatestReleaseApiUri => new("https://api.github.com/repos/BasiliyWolf/DomainAdminConsole/releases/latest");

    public Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;
        try
        {
            using var client = CreateClient(current, TimeSpan.FromSeconds(12));
            using var response = await client.GetAsync(LatestReleaseApiUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(current,
                    $"GitHub вернул HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                    "Проверьте наличие опубликованного Release в репозитории BasiliyWolf/DomainAdminConsole.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var html = root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() : null;
            Uri? page = Uri.TryCreate(html, UriKind.Absolute, out var parsedPage) ? parsedPage : RepositoryUri;

            if (!TryParseVersion(tag, out var latest))
            {
                return new UpdateCheckResult(
                    false, false, current, null, tag, name, page,
                    null, null, null,
                    $"Не удалось определить версию из тега GitHub Release: {tag ?? "<пусто>"}.");
            }

            var asset = SelectUpdateAsset(root);
            var available = Normalize(latest) > Normalize(current);
            return new UpdateCheckResult(
                true, available, current, latest, tag, name, page,
                asset.Name, asset.Uri, asset.Size, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(current, "Истекло время ожидания ответа GitHub.");
        }
        catch (Exception ex)
        {
            return Failed(current, ex.Message);
        }
    }

    public async Task<PreparedUpdateResult> DownloadAndPrepareUpdateAsync(
        UpdateCheckResult result,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!result.CanSelfUpdate || result.AssetDownloadUri is null || string.IsNullOrWhiteSpace(result.AssetName))
            return new PreparedUpdateResult(false, null, "GitHub Release не содержит подходящего файла обновления.");

        var updateRoot = Path.Combine(Path.GetTempPath(), $"DomainAdminConsole.Update.{Guid.NewGuid():N}");
        var downloadDir = Path.Combine(updateRoot, "download");
        var payloadDir = Path.Combine(updateRoot, "payload");
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(payloadDir);

        try
        {
            var safeAssetName = Path.GetFileName(result.AssetName);
            var downloadPath = Path.Combine(downloadDir, safeAssetName);

            using var client = CreateClient(CurrentVersion, TimeSpan.FromMinutes(5));
            using var response = await client.GetAsync(result.AssetDownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? result.AssetSize;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[1024 * 128];
                long received = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    if (total is > 0)
                        progress?.Report(Math.Clamp((int)(received * 100L / total.Value), 0, 100));
                }
            }
            progress?.Report(100);

            string copyRoot;
            if (safeAssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(downloadPath, payloadDir, overwriteFiles: true);
                var exe = Directory.GetFiles(payloadDir, "DomainAdminConsole.exe", SearchOption.AllDirectories).FirstOrDefault()
                          ?? Directory.GetFiles(payloadDir, "DomainAdminConsole*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exe is null)
                    return new PreparedUpdateResult(false, null,
                        "В архиве обновления не найден DomainAdminConsole.exe.");
                copyRoot = Path.GetDirectoryName(exe)!;
            }
            else if (safeAssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var exe = Path.Combine(payloadDir, "DomainAdminConsole.exe");
                File.Copy(downloadPath, exe, overwrite: true);
                copyRoot = payloadDir;
            }
            else
            {
                return new PreparedUpdateResult(false, null,
                    $"Неподдерживаемый формат обновления: {safeAssetName}. Ожидается ZIP или EXE.");
            }

            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "DomainAdminConsole.exe");
            var exeName = Path.GetFileName(currentExe);
            var scriptPath = Path.Combine(Path.GetTempPath(), $"DomainAdminConsole-Updater-{Guid.NewGuid():N}.ps1");

            var script = BuildUpdaterScript(Environment.ProcessId, copyRoot, appDir, exeName, updateRoot, scriptPath);
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
                .ConfigureAwait(false);

            return new PreparedUpdateResult(true, scriptPath, null);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(updateRoot)) Directory.Delete(updateRoot, true); } catch { }
            return new PreparedUpdateResult(false, null, ex.Message);
        }
    }

    public bool LaunchPreparedUpdate(PreparedUpdateResult prepared)
    {
        if (!prepared.Success || string.IsNullOrWhiteSpace(prepared.UpdaterScriptPath) ||
            !File.Exists(prepared.UpdaterScriptPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{prepared.UpdaterScriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenRepository()
        => Process.Start(new ProcessStartInfo(RepositoryUri.ToString()) { UseShellExecute = true });

    public static void OpenRelease(UpdateCheckResult result)
    {
        var uri = result.ReleasePage ?? RepositoryUri;
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private static HttpClient CreateClient(Version current, TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DomainAdminConsole/{current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static (string? Name, Uri? Uri, long? Size) SelectUpdateAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null, null);

        var candidates = new List<(string Name, Uri Uri, long? Size)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            long? size = asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var sizeValue) ? sizeValue : null;
            if (string.IsNullOrWhiteSpace(name) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add((name, uri, size));
        }

        var preferred = candidates.FirstOrDefault(x => x.Name.Equals("DomainAdminConsole-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        if (preferred.Uri is not null) return preferred;

        preferred = candidates.FirstOrDefault(x => x.Name.Equals("DomainAdminConsole.zip", StringComparison.OrdinalIgnoreCase));
        if (preferred.Uri is not null) return preferred;

        preferred = candidates.FirstOrDefault(x => x.Name.Contains("DomainAdminConsole", StringComparison.OrdinalIgnoreCase) &&
                                                   x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (preferred.Uri is not null) return preferred;

        preferred = candidates.FirstOrDefault(x => x.Name.Contains("DomainAdminConsole", StringComparison.OrdinalIgnoreCase) &&
                                                   x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        return preferred.Uri is not null ? preferred : (null, null, null);
    }

    private static string BuildUpdaterScript(int pid, string sourceDir, string appDir, string exeName, string updateRoot, string scriptPath)
    {
        static string Q(string value) => value.Replace("'", "''");

        return $@"
$ErrorActionPreference = 'Stop'
$pidToWait = {pid}
$source = '{Q(sourceDir)}'
$target = '{Q(appDir)}'
$exeName = '{Q(exeName)}'
$updateRoot = '{Q(updateRoot)}'
$scriptSelf = '{Q(scriptPath)}'

try {{ Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue }} catch {{ }}
Start-Sleep -Milliseconds 700

try {{
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {{
        Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
    }}
    Start-Process -FilePath (Join-Path $target $exeName)
    Start-Sleep -Milliseconds 600
    if (Test-Path -LiteralPath $updateRoot) {{ Remove-Item -LiteralPath $updateRoot -Recurse -Force -ErrorAction SilentlyContinue }}
}} catch {{
    $message = $_ | Out-String
    [System.IO.File]::WriteAllText((Join-Path $env:TEMP 'DomainAdminConsole-update-error.txt'), $message)
}}

Remove-Item -LiteralPath $scriptSelf -Force -ErrorAction SilentlyContinue
";
    }

    private static UpdateCheckResult Failed(Version current, string error)
        => new(false, false, current, null, null, null, RepositoryUri, null, null, null, error);

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        if (Version.TryParse(s, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        return false;
    }

    private static Version Normalize(Version v)
        => new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
