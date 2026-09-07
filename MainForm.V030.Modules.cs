using DomainAdminConsole.Models;
using System.ComponentModel;
using System.Diagnostics;

namespace DomainAdminConsole;

public sealed partial class MainForm
{
    private readonly DataGridView _certGrid = Grid();
    private readonly ComboBox _certStore = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _certFilter = new() { Width = 260, PlaceholderText = "Subject / Issuer / Thumbprint" };

    private readonly DataGridView _firewallProfilesGrid = Grid();
    private readonly DataGridView _firewallRulesGrid = Grid();
    private readonly TextBox _firewallFilter = new() { Width = 260, PlaceholderText = "Фильтр правил" };
    private readonly TextBox _fwNewName = new() { Width = 180, PlaceholderText = "Имя нового правила" };
    private readonly TextBox _fwNewPort = new() { Width = 85, PlaceholderText = "Порт" };
    private readonly ComboBox _fwNewProtocol = new() { Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView _smbSessionsGrid = Grid();
    private readonly DataGridView _smbOpenFilesGrid = Grid();

    private readonly DataGridView _devicesGrid = Grid();
    private readonly TextBox _deviceFilter = new() { Width = 300, PlaceholderText = "Имя / класс / производитель / Instance ID" };

    private readonly SortableBindingList<SavedPowerShellScript> _scripts = [];
    private readonly DataGridView _scriptsGrid = Grid();
    private readonly TextBox _scriptName = new() { Width = 210, PlaceholderText = "Название" };
    private readonly TextBox _scriptCategory = new() { Width = 150, PlaceholderText = "Категория" };
    private readonly TextBox _scriptDescription = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly RichTextBox _scriptEditor = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10), AcceptsTab = true, WordWrap = false };
    private string? _editingScriptId;

    private void InitializeV030ModulesState()
    {
        _certStore.Items.AddRange(["Все", "My", "Root", "CA", "WebHosting", "TrustedPeople"]);
        _certStore.SelectedIndex = 0;
        _fwNewProtocol.Items.AddRange(["TCP", "UDP"]);
        _fwNewProtocol.SelectedIndex = 0;
        _firewallFilter.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshFirewallAsync(); } };
        _deviceFilter.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshDevicesAsync(); } };

        foreach (var script in _appData.LoadScripts()) _scripts.Add(script);
        _scriptsGrid.DataSource = _scripts;
        _scriptsGrid.SelectionChanged += (_, _) => LoadSelectedScriptIntoEditor();
    }

    private TabPage BuildCertificatesTab()
    {
        var tab = new TabPage("Сертификаты");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4), WrapContents = false };
        bar.Controls.Add(new Label { Text = @"LocalMachine\\", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_certStore);
        bar.Controls.Add(_certFilter);
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshCertificatesAsync()));
        bar.Controls.Add(Button("Экспорт .CER", async (_, _) => await ExportSelectedCertificateAsync(), 110));
        MirrorToolbarToGridContextMenu(_certGrid, bar);
        tab.Controls.Add(_certGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildFirewallTab()
    {
        var tab = new TabPage("Firewall");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 220);

        var top = new Panel { Dock = DockStyle.Fill };
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        topBar.Controls.Add(Button("Обновить", async (_, _) => await RefreshFirewallAsync()));
        MirrorToolbarToGridContextMenu(_firewallProfilesGrid, topBar);
        top.Controls.Add(_firewallProfilesGrid);
        top.Controls.Add(topBar);

        var bottom = new Panel { Dock = DockStyle.Fill };
        var bottomBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(4), WrapContents = true };
        bottomBar.Controls.Add(_firewallFilter);
        bottomBar.Controls.Add(Button("Найти", async (_, _) => await RefreshFirewallAsync(), 75));
        bottomBar.Controls.Add(Button("Включить", async (_, _) => await SetFirewallRuleEnabledAsync(true), 90));
        bottomBar.Controls.Add(Button("Отключить", async (_, _) => await SetFirewallRuleEnabledAsync(false), 95));
        bottomBar.Controls.Add(_fwNewName);
        bottomBar.Controls.Add(_fwNewProtocol);
        bottomBar.Controls.Add(_fwNewPort);
        bottomBar.Controls.Add(Button("+ Inbound Allow", async (_, _) => await AddFirewallPortRuleAsync(), 120));
        bottomBar.Controls.Add(Button("Удалить правило", async (_, _) => await RemoveFirewallRuleAsync(), 120));
        MirrorToolbarToGridContextMenu(_firewallRulesGrid, bottomBar);
        bottom.Controls.Add(_firewallRulesGrid);
        bottom.Controls.Add(bottomBar);

        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildSmbTab()
    {
        var tab = new TabPage("SMB Sessions / Files");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 350);
        var top = new Panel { Dock = DockStyle.Fill };
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        topBar.Controls.Add(Button("Обновить", async (_, _) => await RefreshSmbAsync()));
        topBar.Controls.Add(Button("Закрыть сессию", async (_, _) => await CloseSmbSessionAsync(), 125));
        MirrorToolbarToGridContextMenu(_smbSessionsGrid, topBar);
        top.Controls.Add(_smbSessionsGrid);
        top.Controls.Add(topBar);
        var bottom = new Panel { Dock = DockStyle.Fill };
        var bottomBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bottomBar.Controls.Add(Button("Обновить файлы", async (_, _) => await RefreshSmbOpenFilesAsync(), 130));
        bottomBar.Controls.Add(Button("Закрыть файл", async (_, _) => await CloseSmbOpenFileAsync(), 110));
        MirrorToolbarToGridContextMenu(_smbOpenFilesGrid, bottomBar);
        bottom.Controls.Add(_smbOpenFilesGrid);
        bottom.Controls.Add(bottomBar);
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildDevicesTab()
    {
        var tab = new TabPage("Устройства / драйверы");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4), WrapContents = false };
        bar.Controls.Add(_deviceFilter);
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshDevicesAsync()));
        bar.Controls.Add(Button("Включить", async (_, _) => await SetSelectedDeviceEnabledAsync(true), 85));
        bar.Controls.Add(Button("Отключить", async (_, _) => await SetSelectedDeviceEnabledAsync(false), 95));
        bar.Controls.Add(Button("Scan devices", async (_, _) => await ScanDevicesAsync(), 105));
        MirrorToolbarToGridContextMenu(_devicesGrid, bar);
        tab.Controls.Add(_devicesGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildScriptLibraryTab()
    {
        var tab = new TabPage("PowerShell-скрипты");
        var split = CreateSafeSplitContainer(Orientation.Vertical, desiredDistance: 380);

        var left = new Panel { Dock = DockStyle.Fill };
        var leftBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(4), WrapContents = true };
        leftBar.Controls.Add(Button("Новый", (_, _) => ClearScriptEditor(), 75));
        leftBar.Controls.Add(Button("Сохранить", (_, _) => SaveScriptFromEditor(), 90));
        leftBar.Controls.Add(Button("Удалить", (_, _) => DeleteSelectedScript(), 80));
        leftBar.Controls.Add(Button("Запустить", async (_, _) => await RunScriptOnCurrentComputerAsync(), 90));
        leftBar.Controls.Add(Button("В массовые", (_, _) => SendScriptToBulk(), 100));
        MirrorToolbarToGridContextMenu(_scriptsGrid, leftBar);
        left.Controls.Add(_scriptsGrid);
        left.Controls.Add(leftBar);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var line1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        line1.Controls.Add(new Label { Text = "Название:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        line1.Controls.Add(_scriptName);
        line1.Controls.Add(new Label { Text = "Категория:", AutoSize = true, Padding = new Padding(5, 6, 0, 0) });
        line1.Controls.Add(_scriptCategory);
        right.Controls.Add(line1, 0, 0);
        right.Controls.Add(new Label { Text = "Описание:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 1);
        right.Controls.Add(_scriptDescription, 0, 2);
        right.Controls.Add(_scriptEditor, 0, 3);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        tab.Controls.Add(split);
        return tab;
    }

    private async Task RefreshCertificatesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var selected = _certStore.SelectedItem?.ToString() ?? "Все";
            var stores = selected == "Все" ? "@('My','Root','CA','WebHosting','TrustedPeople')" : $"@('{PsQuote(selected)}')";
            var filter = PsQuote(_certFilter.Text.Trim());
            BindGrid(_certGrid, await _remote.ExecuteJsonListAsync<CertificateInfoRow>($@"
$stores={stores}
foreach($store in $stores){{
 $path='Cert:\LocalMachine\'+$store
 if(Test-Path $path){{ Get-ChildItem $path -ErrorAction SilentlyContinue | Where-Object {{ '{filter}' -eq '' -or $_.Subject -like '*{filter}*' -or $_.Issuer -like '*{filter}*' -or $_.Thumbprint -like '*{filter}*' }} | ForEach-Object {{
  $dns=''; try {{$dns=($_.DnsNameList | ForEach-Object Unicode) -join ', '}} catch {{}}
  [pscustomobject]@{{Store=$store;Thumbprint=$_.Thumbprint;Subject=$_.Subject;Issuer=$_.Issuer;NotBefore=if($_.NotBefore){{$_.NotBefore.ToString('o')}}else{{$null}};NotAfter=if($_.NotAfter){{$_.NotAfter.ToString('o')}}else{{$null}};HasPrivateKey=$_.HasPrivateKey;FriendlyName=$_.FriendlyName;DnsNames=$dns}}
 }} }}
}}
"));
            WriteAudit("certificates.list", selected, true);
        }
        catch (Exception ex) { WriteAudit("certificates.list", ex.Message, false); ShowError(ex); }
    }

    private async Task ExportSelectedCertificateAsync()
    {
        if (!EnsureConnected() || _certGrid.CurrentRow?.DataBoundItem is not CertificateInfoRow cert) return;
        using var dialog = new SaveFileDialog { Filter = "Certificate (*.cer)|*.cer|All files (*.*)|*.*", FileName = $"{CurrentHost}_{cert.Thumbprint[..Math.Min(12, cert.Thumbprint.Length)]}.cer" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var remoteTemp = $@"C:\Windows\Temp\DAC_{Guid.NewGuid():N}.cer";
        try
        {
            await _remote.ExecuteTextAsync($@"Export-Certificate -Cert 'Cert:\LocalMachine\{PsQuote(cert.Store)}\{PsQuote(cert.Thumbprint)}' -FilePath '{PsQuote(remoteTemp)}' -Type CERT -Force -ErrorAction Stop | Out-Null");
            await Task.Run(() => File.Copy(RemotePathToUnc(CurrentHost, remoteTemp), dialog.FileName, true));
            await _remote.ExecuteTextAsync($"Remove-Item -LiteralPath '{PsQuote(remoteTemp)}' -Force -ErrorAction SilentlyContinue");
            WriteAudit("certificates.export-public", $"{cert.Store} {cert.Thumbprint}", true);
        }
        catch (Exception ex) { try { await _remote.ExecuteTextAsync($"Remove-Item -LiteralPath '{PsQuote(remoteTemp)}' -Force -ErrorAction SilentlyContinue"); } catch { } WriteAudit("certificates.export-public", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshFirewallAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_firewallProfilesGrid, await _remote.ExecuteJsonListAsync<FirewallProfileInfoRow>(@"
Get-NetFirewallProfile -ErrorAction Stop | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;Enabled=[bool]$_.Enabled;DefaultInboundAction=[string]$_.DefaultInboundAction;DefaultOutboundAction=[string]$_.DefaultOutboundAction;NotifyOnListen=[bool]$_.NotifyOnListen;LogAllowed=[bool]$_.LogAllowed;LogBlocked=[bool]$_.LogBlocked} }
"));
            var filter = PsQuote(_firewallFilter.Text.Trim());
            BindGrid(_firewallRulesGrid, await _remote.ExecuteJsonListAsync<FirewallRuleInfoRow>($@"
Get-NetFirewallRule -ErrorAction Stop | Where-Object {{ '{filter}' -eq '' -or $_.DisplayName -like '*{filter}*' -or $_.Name -like '*{filter}*' }} | Select-Object -First 1500 | ForEach-Object {{
 $r=$_; $pf=$r | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue; $af=$r | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue; $sf=$r | Get-NetFirewallServiceFilter -ErrorAction SilentlyContinue
 [pscustomobject]@{{Name=$r.Name;DisplayName=$r.DisplayName;Enabled=[string]$r.Enabled;Direction=[string]$r.Direction;Action=[string]$r.Action;Profile=[string]$r.Profile;Protocol=(($pf.Protocol | Select-Object -Unique) -join ',');LocalPort=(($pf.LocalPort | Select-Object -Unique) -join ',');RemotePort=(($pf.RemotePort | Select-Object -Unique) -join ',');Program=(($af.Program | Select-Object -Unique) -join ',');Service=(($sf.Service | Select-Object -Unique) -join ',')}}
}}
"));
            WriteAudit("firewall.list", _firewallFilter.Text.Trim(), true);
        }
        catch (Exception ex) { WriteAudit("firewall.list", ex.Message, false); ShowError(ex); }
    }

    private async Task SetFirewallRuleEnabledAsync(bool enabled)
    {
        if (!EnsureConnected() || _firewallRulesGrid.CurrentRow?.DataBoundItem is not FirewallRuleInfoRow rule) return;
        try
        {
            await _remote.ExecuteTextAsync($"Set-NetFirewallRule -Name '{PsQuote(rule.Name)}' -Enabled {(enabled ? "True" : "False")} -ErrorAction Stop");
            WriteAudit(enabled ? "firewall.enable" : "firewall.disable", rule.DisplayName, true);
            await RefreshFirewallAsync();
        }
        catch (Exception ex) { WriteAudit(enabled ? "firewall.enable" : "firewall.disable", ex.Message, false); ShowError(ex); }
    }

    private async Task AddFirewallPortRuleAsync()
    {
        if (!EnsureConnected()) return;
        var name = _fwNewName.Text.Trim();
        var port = _fwNewPort.Text.Trim();
        var protocol = _fwNewProtocol.SelectedItem?.ToString() ?? "TCP";
        if (string.IsNullOrWhiteSpace(name) || !int.TryParse(port, out var p) || p is < 1 or > 65535) { MessageBox.Show("Укажите имя правила и корректный порт 1-65535."); return; }
        if (MessageBox.Show($"Создать разрешающее входящее правило {protocol}/{p} «{name}»?", "Firewall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _remote.ExecuteTextAsync($"New-NetFirewallRule -DisplayName '{PsQuote(name)}' -Direction Inbound -Action Allow -Protocol {protocol} -LocalPort {p} -Profile Any -ErrorAction Stop | Out-Null");
            WriteAudit("firewall.create", $"{name} {protocol}/{p}", true);
            _firewallFilter.Text = name;
            await RefreshFirewallAsync();
        }
        catch (Exception ex) { WriteAudit("firewall.create", ex.Message, false); ShowError(ex); }
    }

    private async Task RemoveFirewallRuleAsync()
    {
        if (!EnsureConnected() || _firewallRulesGrid.CurrentRow?.DataBoundItem is not FirewallRuleInfoRow rule) return;
        if (MessageBox.Show($"Удалить правило Firewall «{rule.DisplayName}»?", "Firewall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync($"Remove-NetFirewallRule -Name '{PsQuote(rule.Name)}' -ErrorAction Stop"); WriteAudit("firewall.remove", rule.DisplayName, true); await RefreshFirewallAsync(); }
        catch (Exception ex) { WriteAudit("firewall.remove", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshSmbAsync()
    {
        await RefreshSmbSessionsAsync();
        await RefreshSmbOpenFilesAsync();
    }

    private async Task RefreshSmbSessionsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_smbSessionsGrid, await _remote.ExecuteJsonListAsync<SmbSessionInfoRow>(@"
if(Get-Command Get-SmbSession -ErrorAction SilentlyContinue){ Get-SmbSession | ForEach-Object { [pscustomobject]@{SessionId=[long]$_.SessionId;ClientComputerName=$_.ClientComputerName;ClientUserName=$_.ClientUserName;NumOpens=[int]$_.NumOpens;SecondsExists=[long]$_.SecondsExists;SecondsIdle=[long]$_.SecondsIdle;Dialect=[string]$_.Dialect;Encrypted=[bool]$_.Encrypted} } }
"));
            WriteAudit("smb.sessions", "Список SMB-сессий", true);
        }
        catch (Exception ex) { WriteAudit("smb.sessions", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshSmbOpenFilesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_smbOpenFilesGrid, await _remote.ExecuteJsonListAsync<SmbOpenFileInfoRow>(@"
if(Get-Command Get-SmbOpenFile -ErrorAction SilentlyContinue){ Get-SmbOpenFile | ForEach-Object { [pscustomobject]@{FileId=[long]$_.FileId;SessionId=[long]$_.SessionId;ClientComputerName=$_.ClientComputerName;ClientUserName=$_.ClientUserName;Path=$_.Path;ShareRelativePath=$_.ShareRelativePath;Locks=[int]$_.Locks} } }
"));
            WriteAudit("smb.open-files", "Список открытых SMB-файлов", true);
        }
        catch (Exception ex) { WriteAudit("smb.open-files", ex.Message, false); ShowError(ex); }
    }

    private async Task CloseSmbSessionAsync()
    {
        if (!EnsureConnected() || _smbSessionsGrid.CurrentRow?.DataBoundItem is not SmbSessionInfoRow session) return;
        if (MessageBox.Show($"Принудительно закрыть SMB-сессию {session.ClientUserName} с {session.ClientComputerName}?\r\nОткрытые файлы могут потерять несохранённые данные.", "SMB", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync($"Close-SmbSession -SessionId {session.SessionId} -Force -ErrorAction Stop"); WriteAudit("smb.close-session", $"{session.SessionId} {session.ClientComputerName} {session.ClientUserName}", true); await RefreshSmbAsync(); }
        catch (Exception ex) { WriteAudit("smb.close-session", ex.Message, false); ShowError(ex); }
    }

    private async Task CloseSmbOpenFileAsync()
    {
        if (!EnsureConnected() || _smbOpenFilesGrid.CurrentRow?.DataBoundItem is not SmbOpenFileInfoRow file) return;
        if (MessageBox.Show($"Принудительно закрыть открытый SMB-файл?\r\n{file.Path}", "SMB", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync($"Close-SmbOpenFile -FileId {file.FileId} -Force -ErrorAction Stop"); WriteAudit("smb.close-file", $"{file.FileId} {file.Path}", true); await RefreshSmbOpenFilesAsync(); }
        catch (Exception ex) { WriteAudit("smb.close-file", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshDevicesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var filter = PsQuote(_deviceFilter.Text.Trim());
            BindGrid(_devicesGrid, await _remote.ExecuteJsonListAsync<DeviceInfoRow>($@"
$drivers=@{{}}
Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue | ForEach-Object {{ if($_.DeviceID){{$drivers[$_.DeviceID]=$_}} }}
if(Get-Command Get-PnpDevice -ErrorAction SilentlyContinue){{
 Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {{ '{filter}' -eq '' -or $_.FriendlyName -like '*{filter}*' -or $_.Class -like '*{filter}*' -or $_.InstanceId -like '*{filter}*' }} | ForEach-Object {{
  $d=$drivers[$_.InstanceId]
  [pscustomobject]@{{Status=[string]$_.Status;Class=$_.Class;FriendlyName=$_.FriendlyName;InstanceId=$_.InstanceId;Manufacturer=$d.Manufacturer;DriverProviderName=$d.DriverProviderName;DriverVersion=$d.DriverVersion;DriverDate=if($d.DriverDate){{$d.DriverDate.ToString('yyyy-MM-dd')}}else{{''}};InfName=$d.InfName}}
 }}
}}else{{
 Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object {{ '{filter}' -eq '' -or $_.Name -like '*{filter}*' -or $_.PNPClass -like '*{filter}*' -or $_.PNPDeviceID -like '*{filter}*' }} | ForEach-Object {{ $d=$drivers[$_.PNPDeviceID]; [pscustomobject]@{{Status=$_.Status;Class=$_.PNPClass;FriendlyName=$_.Name;InstanceId=$_.PNPDeviceID;Manufacturer=$_.Manufacturer;DriverProviderName=$d.DriverProviderName;DriverVersion=$d.DriverVersion;DriverDate='';InfName=$d.InfName}} }}
}}
"));
            WriteAudit("devices.list", _deviceFilter.Text.Trim(), true);
        }
        catch (Exception ex) { WriteAudit("devices.list", ex.Message, false); ShowError(ex); }
    }

    private async Task SetSelectedDeviceEnabledAsync(bool enable)
    {
        if (!EnsureConnected() || _devicesGrid.CurrentRow?.DataBoundItem is not DeviceInfoRow device) return;
        if (!enable && MessageBox.Show($"Отключить устройство «{device.FriendlyName}»?\r\n{device.InstanceId}\r\n\r\nОтключение сетевого/дискового устройства может разорвать удалённое подключение.", "Device Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var cmd = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
            await _remote.ExecuteTextAsync($"{cmd} -InstanceId '{PsQuote(device.InstanceId)}' -Confirm:$false -ErrorAction Stop");
            WriteAudit(enable ? "devices.enable" : "devices.disable", device.InstanceId, true);
            await RefreshDevicesAsync();
        }
        catch (Exception ex) { WriteAudit(enable ? "devices.enable" : "devices.disable", ex.Message, false); ShowError(ex); }
    }

    private async Task ScanDevicesAsync()
    {
        if (!EnsureConnected()) return;
        try { var output = await _remote.ExecuteNativeProcessAsync("pnputil.exe", "/scan-devices"); WriteAudit("devices.scan", ShortResult(output), true); await RefreshDevicesAsync(); }
        catch (Exception ex) { WriteAudit("devices.scan", ex.Message, false); ShowError(ex); }
    }

    private void LoadSelectedScriptIntoEditor()
    {
        if (_scriptsGrid.CurrentRow?.DataBoundItem is not SavedPowerShellScript s) return;
        _editingScriptId = s.Id;
        _scriptName.Text = s.Name;
        _scriptCategory.Text = s.Category;
        _scriptDescription.Text = s.Description;
        _scriptEditor.Text = s.Script;
    }

    private void ClearScriptEditor()
    {
        _editingScriptId = null;
        _scriptName.Clear();
        _scriptCategory.Text = "Общие";
        _scriptDescription.Clear();
        _scriptEditor.Clear();
        _scriptName.Focus();
    }

    private void SaveScriptFromEditor()
    {
        var name = _scriptName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(_scriptEditor.Text)) { MessageBox.Show("Введите название и текст PowerShell-скрипта."); return; }
        var existing = _scripts.FirstOrDefault(x => x.Id == _editingScriptId);
        if (existing is null)
        {
            existing = new SavedPowerShellScript { Name = name };
            _scripts.Add(existing);
            _editingScriptId = existing.Id;
        }
        existing.Name = name;
        existing.Category = string.IsNullOrWhiteSpace(_scriptCategory.Text) ? "Общие" : _scriptCategory.Text.Trim();
        existing.Description = _scriptDescription.Text.Trim();
        existing.Script = _scriptEditor.Text;
        existing.UpdatedAt = DateTime.Now;
        _scripts.ResetBindings();
        _appData.SaveScripts(_scripts);
        WriteAudit("scripts.save", $"{existing.Category}/{existing.Name}", true, "LOCAL");
    }

    private void DeleteSelectedScript()
    {
        if (_scriptsGrid.CurrentRow?.DataBoundItem is not SavedPowerShellScript s) return;
        if (MessageBox.Show($"Удалить скрипт «{s.Name}»?", "PowerShell scripts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _scripts.Remove(s);
        _appData.SaveScripts(_scripts);
        WriteAudit("scripts.delete", s.Name, true, "LOCAL");
        ClearScriptEditor();
    }

    private async Task RunScriptOnCurrentComputerAsync()
    {
        if (!EnsureConnected()) return;
        var script = _scriptEditor.Text;
        var name = string.IsNullOrWhiteSpace(_scriptName.Text) ? "Без имени" : _scriptName.Text.Trim();
        if (string.IsNullOrWhiteSpace(script)) return;
        if (MessageBox.Show($"Выполнить скрипт «{name}» на {CurrentHost}?", "PowerShell", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var output = await _remote.ExecuteTextAsync(script);
            _psOutput.AppendText($"\r\n=== SCRIPT: {name} @ {CurrentHost} ===\r\n{output}\r\n");
            _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().First(x => x.Text == "PowerShell");
            WriteAudit("scripts.run", name, true);
        }
        catch (Exception ex) { WriteAudit("scripts.run", $"{name}: {ex.Message}", false); ShowError(ex); }
    }

    private void SendScriptToBulk()
    {
        if (string.IsNullOrWhiteSpace(_scriptEditor.Text)) return;
        _bulkAction.SelectedIndex = 6;
        _bulkCustomScript.Text = _scriptEditor.Text;
        var bulkTab = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Массовые действия");
        if (bulkTab is not null) _tabs.SelectedTab = bulkTab;
    }
}
