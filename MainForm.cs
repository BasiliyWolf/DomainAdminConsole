using DomainAdminConsole.Models;
using DomainAdminConsole.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DomainAdminConsole;

public sealed partial class MainForm : Form
{
    private readonly DomainService _domain = new();
    private readonly RemotePowerShellService _remote = new();
    private readonly BindingList<DomainComputer> _domainComputers = [];
    private readonly BindingList<DomainComputer> _visibleComputers = [];

    private readonly TextBox _target = new() { Width = 260, PlaceholderText = "IP или DNS имя ПК" };
    private readonly Button _connect = new() { Text = "Подключиться", AutoSize = true };
    private readonly Label _connectionStatus = new() { Text = "Не подключено", AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly Label _domainStatus = new() { Text = "Домен: ...", AutoSize = true, Padding = new Padding(8, 7, 0, 0) };

    private readonly TextBox _pcFilter = new() { PlaceholderText = "Фильтр ПК / IP / пользователь", Dock = DockStyle.Fill };
    private readonly CheckBox _onlyActive = new() { Text = "Только активные", Checked = true, AutoSize = true };
    private readonly DataGridView _domainGrid = Grid();
    private readonly Label _scanStatus = new() { AutoSize = true, Text = "" };
    private readonly Button _refreshDomain = new() { Text = "Обновить домен", AutoSize = true };
    private readonly Button _cancelDomainScan = new() { Text = "Стоп", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _domainScanCts;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _diskGrid = Grid();
    private readonly RichTextBox _systemInfo = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
    private readonly RichTextBox _psOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 20, 20), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 10) };
    private readonly TextBox _psInput = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly DataGridView _processGrid = Grid();
    private readonly DataGridView _serviceGrid = Grid();
    private readonly DataGridView _eventGrid = Grid();
    private readonly DataGridView _portGrid = Grid();
    private readonly DataGridView _userSearchGrid = Grid();
    private readonly TextBox _computerUserSearch = new() { Width = 260, PlaceholderText = "Имя ПК или IP" };
    private readonly RichTextBox _computerUserOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
    private readonly RichTextBox _networkOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
    private readonly RichTextBox _sessionOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
    private readonly TextBox _userSearch = new() { Width = 260, PlaceholderText = "DOMAIN\\user или user" };
    private readonly Label _userSearchStatus = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly Button _cancelUserSearch = new() { Text = "Стоп", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _userSearchCts;

    private readonly NotifyIcon _tray = new() { Visible = true, Text = "Domain Admin Console" };
    private bool _reallyExit;

    public MainForm()
    {
        Text = "Domain Admin Console 0.3.0";
        Width = 1500;
        Height = 900;
        MinimumSize = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        ConfigureTray();
        WireEvents();
        InitializeAdvancedState();
        InitializeV030State();
        InitializeV030ModulesState();

        Shown += async (_, _) => await LoadDomainAsync();
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _tray.ShowBalloonTip(1000, "Domain Admin Console", "Приложение свернуто в область уведомлений.", ToolTipIcon.Info);
            }
        };
    }

    private void BuildUi()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 7, 8, 4),
            WrapContents = false
        };
        top.Controls.Add(new Label { Text = "ПК:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        top.Controls.Add(_target);
        top.Controls.Add(_connect);
        top.Controls.Add(_connectionStatus);
        top.Controls.Add(_domainStatus);

        var split = CreateSafeSplitContainer(
            Orientation.Vertical,
            desiredDistance: 390,
            panel1MinSize: 300,
            panel2MinSize: 600);

        split.Panel1.Controls.Add(BuildDomainPanel());
        split.Panel2.Controls.Add(_tabs);

        _tabs.TabPages.Add(BuildOverviewTab());
        _tabs.TabPages.Add(BuildPowerShellTab());
        _tabs.TabPages.Add(BuildFileManagerTab());
        _tabs.TabPages.Add(BuildProcessesTab());
        _tabs.TabPages.Add(BuildServicesTab());
        _tabs.TabPages.Add(BuildEventsTab());
        _tabs.TabPages.Add(BuildPortsTab());
        _tabs.TabPages.Add(BuildNetworkTab());
        _tabs.TabPages.Add(BuildSessionsTab());
        _tabs.TabPages.Add(BuildDomainSearchTab());
        _tabs.TabPages.Add(BuildBulkTab());
        _tabs.TabPages.Add(BuildWakeOnLanTab());
        _tabs.TabPages.Add(BuildFavoritesTab());
        _tabs.TabPages.Add(BuildShadowTab());
        _tabs.TabPages.Add(BuildRegistryTab());
        _tabs.TabPages.Add(BuildScheduledTasksTab());
        _tabs.TabPages.Add(BuildInstalledAppsTab());
        _tabs.TabPages.Add(BuildLocalAccountsTab());
        _tabs.TabPages.Add(BuildNetworkConfigTab());
        _tabs.TabPages.Add(BuildWindowsUpdateTab());
        _tabs.TabPages.Add(BuildBitLockerTpmTab());
        _tabs.TabPages.Add(BuildPrintersTab());
        _tabs.TabPages.Add(BuildCertificatesTab());
        _tabs.TabPages.Add(BuildFirewallTab());
        _tabs.TabPages.Add(BuildSmbTab());
        _tabs.TabPages.Add(BuildDevicesTab());
        _tabs.TabPages.Add(BuildScriptLibraryTab());
        _tabs.TabPages.Add(BuildUtilitiesTab());
        _tabs.TabPages.Add(BuildAuditTab());

        Controls.Add(split);
        Controls.Add(top);
    }

    private Control BuildDomainPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, ColumnCount = 1, RowCount = 3 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        header.Controls.Add(_pcFilter, 0, 0);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(_onlyActive);
        options.Controls.Add(_refreshDomain);
        options.Controls.Add(_cancelDomainScan);
        header.Controls.Add(options, 0, 1);

        var status = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        status.Controls.Add(_scanStatus);
        header.Controls.Add(status, 0, 2);

        _domainGrid.AutoGenerateColumns = false;
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.Name), HeaderText = "ПК", Width = 115 });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.IpAddress), HeaderText = "IP", Width = 95 });
        _domainGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DomainComputer.PingOnline), HeaderText = "Ping", Width = 42 });
        _domainGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DomainComputer.WinRmAvailable), HeaderText = "RM", Width = 38 });
        _domainGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DomainComputer.SmbAvailable), HeaderText = "445", Width = 38 });
        _domainGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(DomainComputer.RdpAvailable), HeaderText = "RDP", Width = 42 });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.Users), HeaderText = "Пользователь", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _domainGrid.DataSource = _visibleComputers;

        panel.Controls.Add(_domainGrid);
        panel.Controls.Add(header);
        return panel;
    }

    private TabPage BuildOverviewTab()
    {
        var tab = new TabPage("Обзор");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 270);
        var diskPanel = new Panel { Dock = DockStyle.Fill };
        var refresh = Button("Обновить", async (_, _) => await RefreshOverviewAsync());
        refresh.Dock = DockStyle.Top;
        _diskGrid.Dock = DockStyle.Fill;
        diskPanel.Controls.Add(_diskGrid);
        diskPanel.Controls.Add(refresh);
        split.Panel1.Controls.Add(diskPanel);
        split.Panel2.Controls.Add(_systemInfo);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildPowerShellTab()
    {
        var tab = new TabPage("PowerShell");
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 38, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var run = Button("Выполнить", async (_, _) => await ExecuteConsoleAsync());
        run.Dock = DockStyle.Fill;
        bottom.Controls.Add(_psInput, 0, 0);
        bottom.Controls.Add(run, 1, 0);
        tab.Controls.Add(_psOutput);
        tab.Controls.Add(bottom);
        return tab;
    }

    private TabPage BuildProcessesTab()
    {
        var tab = new TabPage("Процессы");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        var startPath = new TextBox { Width = 330, PlaceholderText = "notepad.exe или C:\\Path\\app.exe" };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshProcessesAsync()));
        bar.Controls.Add(Button("Завершить PID", async (_, _) => await KillSelectedProcessAsync()));
        bar.Controls.Add(startPath);
        bar.Controls.Add(Button("Запустить", async (_, _) => await StartRemoteProcessAsync(startPath.Text, false)));
        bar.Controls.Add(Button("Запустить у пользователя", async (_, _) => await StartRemoteProcessAsync(startPath.Text, true)));
        tab.Controls.Add(_processGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildServicesTab()
    {
        var tab = new TabPage("Службы");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshServicesAsync()));
        bar.Controls.Add(Button("Запустить", async (_, _) => await ServiceActionAsync("Start-Service")));
        bar.Controls.Add(Button("Остановить", async (_, _) => await ServiceActionAsync("Stop-Service -Force")));
        bar.Controls.Add(Button("Перезапустить", async (_, _) => await ServiceActionAsync("Restart-Service -Force")));
        tab.Controls.Add(_serviceGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildEventsTab()
    {
        var tab = new TabPage("Журналы Windows");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        var log = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        log.Items.AddRange(["System", "Application", "Security"]);
        log.SelectedIndex = 0;
        var count = new NumericUpDown { Minimum = 20, Maximum = 2000, Value = 200, Width = 80 };
        bar.Controls.Add(new Label { Text = "Журнал:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(log);
        bar.Controls.Add(new Label { Text = "Записей:", AutoSize = true, Padding = new Padding(5, 7, 0, 0) });
        bar.Controls.Add(count);
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshEventsAsync(log.Text, (int)count.Value)));
        tab.Controls.Add(_eventGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildPortsTab()
    {
        var tab = new TabPage("Порты");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshPortsAsync()));
        tab.Controls.Add(_portGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildNetworkTab()
    {
        var tab = new TabPage("Ping / Tracert");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        var address = new TextBox { Width = 300, PlaceholderText = "Адрес или DNS имя" };
        var source = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        source.Items.AddRange(["С удаленного ПК", "С этого ПК"]);
        source.SelectedIndex = 0;
        bar.Controls.Add(address);
        bar.Controls.Add(source);
        bar.Controls.Add(Button("Ping", async (_, _) => await RunNetworkToolAsync("ping", address.Text, source.SelectedIndex == 0)));
        bar.Controls.Add(Button("Tracert", async (_, _) => await RunNetworkToolAsync("tracert", address.Text, source.SelectedIndex == 0)));
        tab.Controls.Add(_networkOutput);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildSessionsTab()
    {
        var tab = new TabPage("Сеансы");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshSessionsAsync()));
        tab.Controls.Add(_sessionOutput);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildDomainSearchTab()
    {
        var tab = new TabPage("Поиск в домене");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 390);

        var userToPcPanel = new Panel { Dock = DockStyle.Fill };
        var userBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(4) };
        userBar.Controls.Add(new Label { Text = "Пользователь → ПК:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        userBar.Controls.Add(_userSearch);
        userBar.Controls.Add(Button("Найти во всем домене", async (_, _) => await SearchUserAcrossDomainAsync(), 160));
        userBar.Controls.Add(_cancelUserSearch);
        userBar.Controls.Add(_userSearchStatus);
        _userSearchGrid.AutoGenerateColumns = true;
        userToPcPanel.Controls.Add(_userSearchGrid);
        userToPcPanel.Controls.Add(userBar);

        var pcToUserPanel = new Panel { Dock = DockStyle.Fill };
        var pcBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(4) };
        pcBar.Controls.Add(new Label { Text = "ПК → пользователи:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        pcBar.Controls.Add(_computerUserSearch);
        pcBar.Controls.Add(Button("Найти пользователей", async (_, _) => await SearchUsersOnComputerAsync(), 150));
        pcToUserPanel.Controls.Add(_computerUserOutput);
        pcToUserPanel.Controls.Add(pcBar);

        split.Panel1.Controls.Add(userToPcPanel);
        split.Panel2.Controls.Add(pcToUserPanel);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildUtilitiesTab()
    {
        var tab = new TabPage("Утилиты");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), FlowDirection = FlowDirection.LeftToRight };
        flow.Controls.Add(Button("RDP", (_, _) => LaunchLocal("mstsc.exe", $"/v:{CurrentHost}"), 170, 44));
        flow.Controls.Add(Button("Открыть C$", (_, _) => LaunchLocal("explorer.exe", $"\\\\{CurrentHost}\\c$"), 170, 44));
        flow.Controls.Add(Button("Computer Management", (_, _) => LaunchLocal("compmgmt.msc", $"/computer={CurrentHost}"), 170, 44));
        flow.Controls.Add(Button("Event Viewer", (_, _) => LaunchLocal("eventvwr.msc", $"/computer={CurrentHost}"), 170, 44));
        flow.Controls.Add(Button("GPUpdate /force", async (_, _) => await RunUtilityAsync("gpupdate /force"), 170, 44));
        flow.Controls.Add(Button("Flush DNS", async (_, _) => await RunUtilityAsync("ipconfig /flushdns"), 170, 44));
        flow.Controls.Add(Button("IPConfig /all", async (_, _) => await RunUtilityAsync("ipconfig /all"), 170, 44));
        flow.Controls.Add(Button("Перезагрузить ПК", async (_, _) => await RestartRemoteAsync(), 170, 44));
        flow.Controls.Add(Button("Системная информация", async (_, _) => await RunUtilityAsync("systeminfo"), 170, 44));
        flow.Controls.Add(Button("Обновить политики + DNS", async (_, _) => await RunUtilityAsync("ipconfig /flushdns; gpupdate /force"), 190, 44));

        var messageBox = new TextBox { Width = 500, PlaceholderText = "Сообщение пользователю (msg *)" };
        flow.Controls.Add(messageBox);
        flow.Controls.Add(Button("Отправить сообщение", async (_, _) => await SendMessageAsync(messageBox.Text), 180, 32));

        tab.Controls.Add(flow);
        return tab;
    }

    private void WireEvents()
    {
        _connect.Click += async (_, _) => await ConnectAsync(_target.Text);
        _target.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ConnectAsync(_target.Text); }
        };
        _psInput.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await ExecuteConsoleAsync();
            }
        };
        _pcFilter.TextChanged += (_, _) => ApplyComputerFilter();
        _onlyActive.CheckedChanged += (_, _) => ApplyComputerFilter();
        _refreshDomain.Click += async (_, _) => await LoadDomainAsync();
        _cancelDomainScan.Click += (_, _) => _domainScanCts?.Cancel();
        _cancelUserSearch.Click += (_, _) => _userSearchCts?.Cancel();
        _bulkCts?.Cancel();
        _domainGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_domainGrid.Rows[e.RowIndex].DataBoundItem is DomainComputer pc)
            {
                _target.Text = string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName;
                await ConnectAsync(_target.Text);
            }
        };
        _userSearchGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_userSearchGrid.Rows[e.RowIndex].DataBoundItem is SessionInfo s)
            {
                _target.Text = s.Computer;
                await ConnectAsync(s.Computer);
            }
        };
    }

    private void ConfigureTray()
    {
        _tray.Icon = SystemIcons.Application;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreFromTray());
        menu.Items.Add("RDP к текущему ПК", null, (_, _) => { if (!string.IsNullOrWhiteSpace(CurrentHost)) LaunchLocal("mstsc.exe", $"/v:{CurrentHost}"); });
        menu.Items.Add("Ping текущего ПК", null, async (_, _) => { if (!string.IsNullOrWhiteSpace(CurrentHost)) await RunNetworkToolAsync("ping", CurrentHost, false); });
        menu.Items.Add("Открыть C$", null, (_, _) => { if (!string.IsNullOrWhiteSpace(CurrentHost)) LaunchLocal("explorer.exe", $"\\\\{CurrentHost}\\c$"); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => { _reallyExit = true; _tray.Visible = false; Close(); });
        ConfigureAdvancedTray(menu);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private async Task LoadDomainAsync()
    {
        _domainScanCts?.Cancel();
        _domainScanCts = new CancellationTokenSource();
        var ct = _domainScanCts.Token;
        _refreshDomain.Enabled = false;
        _cancelDomainScan.Enabled = true;

        try
        {
            _domainStatus.Text = $"Домен: {_domain.GetCurrentDomainName()}";
            _scanStatus.Text = "Получение списка компьютеров из AD...";
            var pcs = await _domain.GetDomainComputersAsync(ct);

            _domainComputers.Clear();
            foreach (var pc in pcs) _domainComputers.Add(pc);
            ApplyComputerFilter();
            _scanStatus.Text = $"AD: {pcs.Count} ПК. Проверка online...";

            int completed = 0;
            var progress = new Progress<DomainComputer>(pc =>
            {
                completed++;
                _scanStatus.Text = $"Проверено {completed}/{pcs.Count}; активных: {_domainComputers.Count(x => x.IsActive)}";
                ApplyComputerFilter(false);
            });
            await _domain.ProbeComputersAsync(pcs, 32, progress, ct);
            _scanStatus.Text = $"AD: {pcs.Count}; online: {pcs.Count(x => x.IsActive)}; WinRM: {pcs.Count(x => x.WinRmAvailable)}; RDP: {pcs.Count(x => x.RdpAvailable)}";
            ApplyComputerFilter();
        }
        catch (OperationCanceledException)
        {
            _scanStatus.Text = "Сканирование остановлено.";
        }
        catch (Exception ex)
        {
            _scanStatus.Text = "Ошибка домена";
            ShowError(ex);
        }
        finally
        {
            _refreshDomain.Enabled = true;
            _cancelDomainScan.Enabled = false;
        }
    }

    private void ApplyComputerFilter(bool reset = true)
    {
        var filter = _pcFilter.Text.Trim();
        var selected = reset ? (_domainGrid.CurrentRow?.DataBoundItem as DomainComputer)?.Name : null;
        _visibleComputers.RaiseListChangedEvents = false;
        _visibleComputers.Clear();
        foreach (var pc in _domainComputers.Where(pc =>
                     (!_onlyActive.Checked || pc.IsActive) &&
                     (string.IsNullOrWhiteSpace(filter)
                      || pc.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                      || pc.DnsHostName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                      || pc.IpAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
                      || pc.Users.Contains(filter, StringComparison.OrdinalIgnoreCase))))
            _visibleComputers.Add(pc);
        _visibleComputers.RaiseListChangedEvents = true;
        _visibleComputers.ResetBindings();

        if (!string.IsNullOrWhiteSpace(selected))
        {
            foreach (DataGridViewRow row in _domainGrid.Rows)
                if ((row.DataBoundItem as DomainComputer)?.Name == selected) { row.Selected = true; break; }
        }
    }

    private async Task ConnectAsync(string host)
    {
        host = host.Trim();
        if (string.IsNullOrWhiteSpace(host)) return;
        UseWaitCursor = true;
        _connect.Enabled = false;
        _connectionStatus.Text = $"Подключение к {host}...";
        try
        {
            var connectionHost = await ResolveConnectionHostAsync(host);
            await _remote.ConnectAsync(connectionHost);
            _connectionStatus.Text = connectionHost.Equals(host, StringComparison.OrdinalIgnoreCase)
                ? $"Подключено: {connectionHost}"
                : $"Подключено: {connectionHost} (введено {host})";
            _target.Text = host;
            _psOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Подключено к {connectionHost}\r\n");
            WriteAudit("connection.connect", $"Введено: {host}; WinRM: {connectionHost}", true, connectionHost);
            await RefreshOverviewAsync();
            await RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            _connectionStatus.Text = "Ошибка подключения";
            WriteAudit("connection.connect", $"{host}: {ex.Message}", false, host);
            ShowError(ex);
        }
        finally
        {
            _connect.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private async Task RefreshOverviewAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var disks = await _remote.ExecuteJsonListAsync<DiskInfo>(@"
Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
  [pscustomobject]@{
    Name=$_.DeviceID; Label=$_.VolumeName;
    SizeGb=[math]::Round($_.Size/1GB,2);
    FreeGb=[math]::Round($_.FreeSpace/1GB,2);
    FreePercent=if($_.Size){[math]::Round(($_.FreeSpace/$_.Size)*100,1)}else{0}
  }
}
");
            _diskGrid.DataSource = disks;
            _systemInfo.Text = await _remote.ExecuteTextAsync(@"
$os=Get-CimInstance Win32_OperatingSystem
$cs=Get-CimInstance Win32_ComputerSystem
$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
[pscustomobject]@{
 Computer=$env:COMPUTERNAME
 User=$cs.UserName
 OS=$os.Caption
 Version=$os.Version
 Uptime=((Get-Date)-$os.LastBootUpTime).ToString()
 CPU=$cpu.Name
 RAM_GB=[math]::Round($cs.TotalPhysicalMemory/1GB,2)
 Domain=$cs.Domain
 Manufacturer=$cs.Manufacturer
 Model=$cs.Model
} | Format-List
");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task ExecuteConsoleAsync()
    {
        if (!EnsureConnected()) return;
        var command = _psInput.Text;
        if (string.IsNullOrWhiteSpace(command)) return;
        _psInput.Clear();
        _psOutput.AppendText($"PS {CurrentHost}> {command}\r\n");
        try
        {
            var output = await _remote.ExecuteTextAsync(command);
            _psOutput.AppendText(output + (output.EndsWith("\n") ? "" : "\r\n"));
            WriteAudit("powershell.execute", $"Команда выполнена; длина={command.Length}", true);
        }
        catch (Exception ex) { WriteAudit("powershell.execute", $"Ошибка выполнения команды; длина={command.Length}; {ex.Message}", false); _psOutput.AppendText($"ERROR: {ex.Message}\r\n"); }
        _psOutput.SelectionStart = _psOutput.TextLength;
        _psOutput.ScrollToCaret();
    }

    private async Task RefreshProcessesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _processGrid.DataSource = await _remote.ExecuteJsonListAsync<ProcessInfo>(@"
Get-Process -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | ForEach-Object {
 [pscustomobject]@{ Id=$_.Id; Name=$_.ProcessName; Cpu=[math]::Round([double]$_.CPU,1); MemoryMb=[math]::Round($_.WorkingSet64/1MB,1); UserName='' }
}
");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task KillSelectedProcessAsync()
    {
        if (!EnsureConnected()) return;
        if (_processGrid.CurrentRow?.DataBoundItem is not ProcessInfo p) return;
        if (MessageBox.Show($"Завершить процесс {p.Name} ({p.Id})?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _remote.ExecuteTextAsync($"Stop-Process -Id {p.Id} -Force -ErrorAction Stop");
            WriteAudit("process.kill", $"{p.Name} PID={p.Id}", true);
            await RefreshProcessesAsync();
        }
        catch (Exception ex) { WriteAudit("process.kill", $"{p.Name} PID={p.Id}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task StartRemoteProcessAsync(string path, bool interactive)
    {
        if (!EnsureConnected() || string.IsNullOrWhiteSpace(path)) return;
        var escaped = PsQuote(path.Trim());
        try
        {
            if (!interactive)
            {
                await _remote.ExecuteTextAsync($"Start-Process -FilePath '{escaped}' -ErrorAction Stop");
            }
            else
            {
                var script = $@"
$user=(Get-CimInstance Win32_ComputerSystem).UserName
if(-not $user){{ throw 'Нет интерактивного консольного пользователя.' }}
$name='DomainAdminConsole_'+[guid]::NewGuid().ToString('N')
$action=New-ScheduledTaskAction -Execute '{escaped}'
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $name
Start-Sleep -Milliseconds 800
Unregister-ScheduledTask -TaskName $name -Confirm:$false
";
                await _remote.ExecuteTextAsync(script);
            }
            WriteAudit(interactive ? "process.start-interactive" : "process.start", path, true);
            await RefreshProcessesAsync();
        }
        catch (Exception ex) { WriteAudit(interactive ? "process.start-interactive" : "process.start", $"{path}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshServicesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _serviceGrid.DataSource = await _remote.ExecuteJsonListAsync<ServiceInfo>(@"
Get-CimInstance Win32_Service | Sort-Object DisplayName | ForEach-Object {
 [pscustomobject]@{ Name=$_.Name; DisplayName=$_.DisplayName; Status=$_.State; StartType=$_.StartMode }
}
");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task ServiceActionAsync(string action)
    {
        if (!EnsureConnected()) return;
        if (_serviceGrid.CurrentRow?.DataBoundItem is not ServiceInfo s) return;
        try
        {
            await _remote.ExecuteTextAsync($"{action} -Name '{PsQuote(s.Name)}' -ErrorAction Stop");
            WriteAudit("service.action", $"{action}; {s.Name}", true);
            await RefreshServicesAsync();
        }
        catch (Exception ex) { WriteAudit("service.action", $"{action}; {s.Name}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshEventsAsync(string log, int count)
    {
        if (!EnsureConnected()) return;
        try
        {
            _eventGrid.DataSource = await _remote.ExecuteJsonListAsync<EventRow>($@"
Get-WinEvent -LogName '{PsQuote(log)}' -MaxEvents {count} -ErrorAction Stop | ForEach-Object {{
 [pscustomobject]@{{ TimeCreated=$_.TimeCreated.ToString('o'); Id=$_.Id; Level=$_.LevelDisplayName; Provider=$_.ProviderName; Message=$_.Message }}
}}
");
            if (_eventGrid.Columns[nameof(EventRow.Message)] is { } col) col.Width = 650;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RefreshPortsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _portGrid.DataSource = await _remote.ExecuteJsonListAsync<PortInfo>(@"
$tcp=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | ForEach-Object {
 $p=Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
 [pscustomobject]@{Protocol='TCP';LocalAddress=$_.LocalAddress;LocalPort=$_.LocalPort;Process=$p.ProcessName;Pid=$_.OwningProcess}
}
$udp=Get-NetUDPEndpoint -ErrorAction SilentlyContinue | ForEach-Object {
 $p=Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
 [pscustomobject]@{Protocol='UDP';LocalAddress=$_.LocalAddress;LocalPort=$_.LocalPort;Process=$p.ProcessName;Pid=$_.OwningProcess}
}
$tcp+$udp | Sort-Object LocalPort
");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RunNetworkToolAsync(string tool, string address, bool remote)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        address = address.Trim();
        _networkOutput.Text = $"> {tool} {address}\r\n\r\n";
        try
        {
            string output;
            if (remote)
            {
                if (!EnsureConnected()) return;
                output = await _remote.ExecuteTextAsync($"{tool} {PsCmdArg(address)}");
            }
            else
            {
                output = await RunLocalCommandAsync(tool == "ping" ? "ping.exe" : "tracert.exe", address);
            }
            _networkOutput.AppendText(output);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RefreshSessionsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _sessionOutput.Text = await _remote.ExecuteTextAsync(@"
'=== Console user ==='
(Get-CimInstance Win32_ComputerSystem).UserName
''
'=== QUSER ==='
quser 2>&1
");

            var pc = _domainComputers.FirstOrDefault(x => x.Name.Equals(CurrentHost, StringComparison.OrdinalIgnoreCase) || x.DnsHostName.Equals(CurrentHost, StringComparison.OrdinalIgnoreCase));
            if (pc is not null)
            {
                var console = await _remote.ExecuteTextAsync("(Get-CimInstance Win32_ComputerSystem).UserName");
                pc.Users = console.Trim();
                ApplyComputerFilter(false);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task SearchUserAcrossDomainAsync()
    {
        var user = _userSearch.Text.Trim();
        if (string.IsNullOrWhiteSpace(user)) return;
        if (_domainComputers.Count == 0)
        {
            MessageBox.Show("Сначала загрузите список компьютеров домена.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _userSearchCts?.Cancel();
        _bulkCts?.Cancel();
        _userSearchCts = new CancellationTokenSource();
        _cancelUserSearch.Enabled = true;
        _userSearchStatus.Text = "Поиск...";
        try
        {
            var progress = new Progress<string>(x => _userSearchStatus.Text = x);
            var result = await _domain.FindUserAcrossDomainAsync(_domainComputers, user, 20, progress, _userSearchCts.Token);
            _userSearchGrid.DataSource = result;
            _userSearchStatus.Text = $"Найдено ПК: {result.Count}";
        }
        catch (OperationCanceledException) { _userSearchStatus.Text = "Поиск остановлен."; }
        catch (Exception ex) { ShowError(ex); }
        finally { _cancelUserSearch.Enabled = false; }
    }


    private async Task SearchUsersOnComputerAsync()
    {
        var host = _computerUserSearch.Text.Trim();
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            var connectionHost = await ResolveConnectionHostAsync(host);
            var sessions = await _domain.GetUsersOnComputerAsync(connectionHost);
            if (sessions.Count == 0)
            {
                _computerUserOutput.Text = "Пользователи не найдены.";
                return;
            }

            _computerUserOutput.Text = string.Join(Environment.NewLine + Environment.NewLine, sessions.Select(s =>
                $"ПК: {s.Computer}\r\nConsole: {s.User}\r\n{s.Raw}"));
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static async Task<string> ResolveConnectionHostAsync(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return host;

        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return string.IsNullOrWhiteSpace(entry.HostName) ? host : entry.HostName;
        }
        catch
        {
            return host;
        }
    }

    private async Task RunUtilityAsync(string command)
    {
        if (!EnsureConnected()) return;
        try
        {
            var output = await _remote.ExecuteTextAsync(command);
            _psOutput.AppendText($"\r\nPS {CurrentHost}> {command}\r\n{output}\r\n");
            _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().First(x => x.Text == "PowerShell");
            WriteAudit("utility.execute", command, true);
        }
        catch (Exception ex) { WriteAudit("utility.execute", $"{command}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RestartRemoteAsync()
    {
        if (!EnsureConnected()) return;
        if (MessageBox.Show($"Перезагрузить {CurrentHost}?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync("Restart-Computer -Force"); WriteAudit("computer.restart", "Restart-Computer -Force", true); }
        catch (Exception ex) { WriteAudit("computer.restart", ex.Message, false); MessageBox.Show($"Команда отправлена или соединение разорвано: {ex.Message}"); }
    }

    private async Task SendMessageAsync(string message)
    {
        if (!EnsureConnected() || string.IsNullOrWhiteSpace(message)) return;
        try { await _remote.ExecuteTextAsync($"msg * '{PsQuote(message)}'"); WriteAudit("message.send", $"Длина сообщения: {message.Length}", true); }
        catch (Exception ex) { WriteAudit("message.send", ex.Message, false); ShowError(ex); }
    }

    private string CurrentHost => _remote.ConnectedHost ?? _target.Text.Trim();

    private bool EnsureConnected()
    {
        if (_remote.IsConnected) return true;
        MessageBox.Show("Сначала подключитесь к удаленному ПК.", "Domain Admin Console", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    /// <summary>
    /// Creates a SplitContainer without assigning SplitterDistance while the control
    /// still has its design-time/default size. WinForms validates SplitterDistance
    /// immediately, so assigning it in an object initializer can throw before Dock
    /// layout gives the control its real size.
    /// </summary>
    private static SplitContainer CreateSafeSplitContainer(
        Orientation orientation,
        int desiredDistance,
        int panel1MinSize = 100,
        int panel2MinSize = 100)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = orientation,
            SplitterWidth = 6
        };

        var configured = false;

        void ApplyLayoutIfPossible()
        {
            if (configured || split.IsDisposed)
                return;

            var totalSize = orientation == Orientation.Vertical
                ? split.ClientSize.Width
                : split.ClientSize.Height;

            // Wait until Dock layout has provided enough real space.
            if (totalSize <= panel1MinSize + panel2MinSize + split.SplitterWidth)
                return;

            var maxDistance = totalSize - panel2MinSize - split.SplitterWidth;
            if (maxDistance < panel1MinSize)
                return;

            var distance = Math.Clamp(desiredDistance, panel1MinSize, maxDistance);

            // Set the distance first while WinForms still uses its default minimums,
            // then apply our minimum sizes. At this point both panels already fit.
            split.SplitterDistance = distance;
            split.Panel1MinSize = panel1MinSize;
            split.Panel2MinSize = panel2MinSize;
            configured = true;
        }

        split.SizeChanged += (_, _) => ApplyLayoutIfPossible();
        split.VisibleChanged += (_, _) => ApplyLayoutIfPossible();
        split.HandleCreated += (_, _) =>
        {
            if (!split.IsDisposed && split.IsHandleCreated)
                split.BeginInvoke(new Action(ApplyLayoutIfPossible));
        };

        return split;
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window
    };

    private static Button Button(string text, EventHandler handler, int width = 100, int height = 28)
    {
        var b = new Button { Text = text, Width = width, Height = height, AutoSize = width == 100 };
        b.Click += handler;
        return b;
    }

    private static string PsQuote(string s) => s.Replace("'", "''");
    private static string PsCmdArg(string s) => $"'{PsQuote(s)}'";

    private static async Task<string> RunLocalCommandAsync(string file, string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (await stdout) + (await stderr);
    }

    private static void LaunchLocal(string file, string arguments)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void ShowError(Exception ex)
        => MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _domainScanCts?.Cancel();
        _userSearchCts?.Cancel();
        _bulkCts?.Cancel();
        _remote.Dispose();
        _tray.Visible = false;
    }
}
