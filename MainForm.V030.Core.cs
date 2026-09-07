using DomainAdminConsole.Models;
using DomainAdminConsole.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DomainAdminConsole;

public sealed partial class MainForm
{
    private readonly DataGridView _localFilesGrid = Grid();
    private readonly DataGridView _remoteFilesGrid = Grid();
    private readonly TextBox _localPath = new() { Width = 430, Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
    private readonly TextBox _remotePath = new() { Width = 430, Text = @"C:\" };
    private List<FileManagerEntry> _localFiles = [];
    private List<FileManagerEntry> _remoteFiles = [];

    private readonly SortableBindingList<BulkTarget> _bulkTargets = [];
    private readonly SortableBindingList<BulkResult> _bulkResults = [];
    private readonly DataGridView _bulkTargetsGrid = Grid(readOnly: false, multiSelect: true, autoGenerateColumns: false);
    private readonly DataGridView _bulkResultsGrid = Grid();
    private readonly ComboBox _bulkAction = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _bulkFavoriteGroup = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _bulkCustomScript = new() { Width = 430, PlaceholderText = "PowerShell-команда для каждого ПК" };
    private readonly NumericUpDown _bulkConcurrency = new() { Width = 60, Minimum = 1, Maximum = 64, Value = 10 };
    private readonly Label _bulkStatus = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private CancellationTokenSource? _bulkCts;

    private readonly SortableBindingList<KnownMacInfo> _knownMacs = [];
    private readonly DataGridView _wolGrid = Grid();
    private readonly TextBox _wolMac = new() { Width = 180, PlaceholderText = "AA-BB-CC-DD-EE-FF" };
    private readonly TextBox _wolBroadcast = new() { Width = 150, Text = "255.255.255.255" };
    private readonly NumericUpDown _wolPort = new() { Width = 60, Minimum = 1, Maximum = 65535, Value = 9 };

    private readonly DataGridView _bitLockerGrid = Grid();
    private readonly RichTextBox _tpmInfo = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };

    private readonly DataGridView _printersGrid = Grid();
    private readonly DataGridView _printJobsGrid = Grid();

    private void InitializeV030State()
    {
        ConfigureFileGrid(_localFilesGrid);
        ConfigureFileGrid(_remoteFilesGrid);
        _localFilesGrid.CellDoubleClick += (_, e) => LocalFileDoubleClick(e.RowIndex);
        _remoteFilesGrid.CellDoubleClick += async (_, e) => await RemoteFileDoubleClickAsync(e.RowIndex);

        _bulkAction.Items.AddRange([
            "GPUpdate /force", "Перезапустить Print Spooler", "Flush DNS",
            "Windows Update Scan", "Перезагрузить ПК", "Выключить ПК", "Своя PowerShell команда"
        ]);
        _bulkAction.SelectedIndex = 0;
        _bulkAction.SelectedIndexChanged += (_, _) => _bulkCustomScript.Enabled = _bulkAction.SelectedIndex == 6;
        _bulkCustomScript.Enabled = false;

        _bulkTargetsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(BulkTarget.Selected), HeaderText = "✓", Width = 38 });
        _bulkTargetsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BulkTarget.Name), HeaderText = "ПК", Width = 140, ReadOnly = true });
        _bulkTargetsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BulkTarget.Host), HeaderText = "DNS / IP", Width = 210, ReadOnly = true });
        _bulkTargetsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BulkTarget.IpAddress), HeaderText = "IP", Width = 110, ReadOnly = true });
        _bulkTargetsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BulkTarget.Users), HeaderText = "Пользователь", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _bulkTargetsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(BulkTarget.WinRmAvailable), HeaderText = "WinRM", Width = 60, ReadOnly = true });
        _bulkTargetsGrid.DataSource = _bulkTargets;
        _bulkResultsGrid.DataSource = _bulkResults;
        RefreshBulkGroups();

        foreach (var mac in _appData.LoadKnownMacs()) _knownMacs.Add(mac);
        _wolGrid.DataSource = _knownMacs;
        RefreshLocalFiles();
        _wolGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _wolGrid.Rows[e.RowIndex].DataBoundItem is KnownMacInfo mac)
                _wolMac.Text = mac.MacAddress;
        };
    }

    private static void ConfigureFileGrid(DataGridView grid)
    {
        grid.AutoGenerateColumns = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FileManagerEntry.Type), HeaderText = "Тип", Width = 65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FileManagerEntry.Name), HeaderText = "Имя", Width = 260 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FileManagerEntry.SizeMb), HeaderText = "МБ", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FileManagerEntry.LastWriteTime), HeaderText = "Изменён", Width = 145 });
    }

    private TabPage BuildFileManagerTab()
    {
        var tab = new TabPage("Файловый менеджер");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(BuildLocalFilesPanel(), 0, 0);
        root.Controls.Add(BuildRemoteFilesPanel(), 1, 0);
        tab.Controls.Add(root);
        return tab;
    }

    private Control BuildLocalFilesPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var caption = new Label { Text = "Локальный компьютер", Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(2), WrapContents = true };
        bar.Controls.Add(_localPath);
        bar.Controls.Add(Button("Открыть", (_, _) => RefreshLocalFiles(), 75));
        bar.Controls.Add(Button("Вверх", (_, _) => LocalPathUp(), 70));
        bar.Controls.Add(Button("→ На ПК", async (_, _) => await UploadSelectedToRemoteAsync(), 90));
        bar.Controls.Add(Button("Новая папка", (_, _) => CreateLocalFolder(), 105));
        bar.Controls.Add(Button("Переименовать", (_, _) => RenameLocalEntry(), 120));
        bar.Controls.Add(Button("Удалить", (_, _) => DeleteLocalEntry(), 80));
        bar.Controls.Add(Button("Explorer", (_, _) => OpenLocalPathInExplorer(), 80));
        panel.Controls.Add(_localFilesGrid);
        panel.Controls.Add(bar);
        panel.Controls.Add(caption);
        return panel;
    }

    private Control BuildRemoteFilesPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var caption = new Label { Text = "Удалённый компьютер", Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(2), WrapContents = true };
        bar.Controls.Add(_remotePath);
        bar.Controls.Add(Button("Открыть", async (_, _) => await RefreshRemoteFilesAsync(), 75));
        bar.Controls.Add(Button("Вверх", async (_, _) => await RemotePathUpAsync(), 70));
        bar.Controls.Add(Button("← На этот ПК", async (_, _) => await DownloadSelectedToLocalAsync(), 100));
        bar.Controls.Add(Button("Новая папка", async (_, _) => await CreateRemoteFolderAsync(), 105));
        bar.Controls.Add(Button("Переименовать", async (_, _) => await RenameRemoteEntryAsync(), 120));
        bar.Controls.Add(Button("Удалить", async (_, _) => await DeleteRemoteEntryAsync(), 80));
        bar.Controls.Add(Button("Explorer", (_, _) => OpenCurrentRemotePathInExplorer(), 80));
        panel.Controls.Add(_remoteFilesGrid);
        panel.Controls.Add(bar);
        panel.Controls.Add(caption);
        return panel;
    }

    private TabPage BuildBulkTab()
    {
        var tab = new TabPage("Массовые действия");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 430);
        var top = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(4), WrapContents = true };
        bar.Controls.Add(Button("Активные ПК", (_, _) => LoadBulkTargets(true), 105));
        bar.Controls.Add(Button("Все ПК AD", (_, _) => LoadBulkTargets(false), 100));
        bar.Controls.Add(new Label { Text = "Группа:", AutoSize = true, Padding = new Padding(5, 7, 0, 0) });
        bar.Controls.Add(_bulkFavoriteGroup);
        bar.Controls.Add(Button("Загрузить группу", (_, _) => LoadBulkFavoriteGroup(), 120));
        bar.Controls.Add(Button("Отметить все", (_, _) => SetAllBulkTargets(true), 105));
        bar.Controls.Add(Button("Снять все", (_, _) => SetAllBulkTargets(false), 95));
        bar.Controls.Add(new Label { Text = "Параллельно:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_bulkConcurrency);
        bar.Controls.Add(_bulkAction);
        bar.Controls.Add(_bulkCustomScript);
        bar.Controls.Add(Button("Выполнить", async (_, _) => await RunBulkActionAsync(), 100));
        bar.Controls.Add(Button("Стоп", (_, _) => _bulkCts?.Cancel(), 70));
        bar.Controls.Add(_bulkStatus);
        top.Controls.Add(_bulkTargetsGrid);
        top.Controls.Add(bar);
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(_bulkResultsGrid);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildWakeOnLanTab()
    {
        var tab = new TabPage("Wake-on-LAN");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(6), WrapContents = true };
        bar.Controls.Add(new Label { Text = "MAC:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_wolMac);
        bar.Controls.Add(new Label { Text = "Broadcast:", AutoSize = true, Padding = new Padding(5, 7, 0, 0) });
        bar.Controls.Add(_wolBroadcast);
        bar.Controls.Add(new Label { Text = "UDP:", AutoSize = true, Padding = new Padding(5, 7, 0, 0) });
        bar.Controls.Add(_wolPort);
        bar.Controls.Add(Button("Отправить Magic Packet", async (_, _) => await SendWakeOnLanAsync(), 165));
        bar.Controls.Add(Button("Обновить список", (_, _) => ReloadKnownMacs(), 125));
        bar.Controls.Add(new Label { Text = "MAC-адреса сохраняются при просмотре сетевых интерфейсов доступных ПК.", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        tab.Controls.Add(_wolGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildBitLockerTpmTab()
    {
        var tab = new TabPage("BitLocker / TPM");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 390);
        var top = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshBitLockerTpmAsync()));
        bar.Controls.Add(Button("Suspend 1 reboot", async (_, _) => await SetBitLockerProtectionAsync(false), 130));
        bar.Controls.Add(Button("Resume", async (_, _) => await SetBitLockerProtectionAsync(true), 90));
        top.Controls.Add(_bitLockerGrid);
        top.Controls.Add(bar);
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(_tpmInfo);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildPrintersTab()
    {
        var tab = new TabPage("Принтеры");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 380);
        var p1 = new Panel { Dock = DockStyle.Fill };
        var bar1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar1.Controls.Add(Button("Обновить", async (_, _) => await RefreshPrintersAsync()));
        bar1.Controls.Add(Button("Очередь", async (_, _) => await RefreshPrintJobsAsync(), 85));
        bar1.Controls.Add(Button("Restart Spooler", async (_, _) => await RestartSpoolerAsync(), 125));
        p1.Controls.Add(_printersGrid);
        p1.Controls.Add(bar1);
        var p2 = new Panel { Dock = DockStyle.Fill };
        var bar2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar2.Controls.Add(Button("Обновить задания", async (_, _) => await RefreshPrintJobsAsync(), 130));
        bar2.Controls.Add(Button("Отменить задание", async (_, _) => await CancelPrintJobAsync(), 135));
        p2.Controls.Add(_printJobsGrid);
        p2.Controls.Add(bar2);
        split.Panel1.Controls.Add(p1);
        split.Panel2.Controls.Add(p2);
        tab.Controls.Add(split);
        return tab;
    }

    private void RefreshLocalFiles()
    {
        try
        {
            var path = _localPath.Text.Trim();
            var di = new DirectoryInfo(path);
            _localFiles = di.EnumerateFileSystemInfos()
                .OrderByDescending(x => x is DirectoryInfo).ThenBy(x => x.Name)
                .Select(x => new FileManagerEntry
                {
                    Name = x.Name, FullName = x.FullName, IsDirectory = x is DirectoryInfo,
                    SizeBytes = x is FileInfo f ? f.Length : 0, LastWriteTime = x.LastWriteTime
                }).ToList();
            BindGrid(_localFilesGrid, _localFiles);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LocalFileDoubleClick(int rowIndex)
    {
        if (rowIndex < 0 || _localFilesGrid.Rows[rowIndex].DataBoundItem is not FileManagerEntry item) return;
        if (item.IsDirectory) { _localPath.Text = item.FullName; RefreshLocalFiles(); }
        else LaunchLocal(item.FullName, "");
    }

    private void LocalPathUp()
    {
        try { var p = Directory.GetParent(_localPath.Text.Trim()); if (p is not null) { _localPath.Text = p.FullName; RefreshLocalFiles(); } }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RefreshRemoteFilesAsync()
    {
        if (!EnsureConnected()) return;
        var path = _remotePath.Text.Trim();
        try
        {
            _remoteFiles = await _remote.ExecuteJsonListAsync<FileManagerEntry>($@"
Get-ChildItem -LiteralPath '{PsQuote(path)}' -Force -ErrorAction Stop | Sort-Object @{{Expression={{-not $_.PSIsContainer}}}},Name | ForEach-Object {{
 [pscustomobject]@{{Name=$_.Name;FullName=$_.FullName;IsDirectory=$_.PSIsContainer;SizeBytes=if($_.PSIsContainer){{0}}else{{$_.Length}};LastWriteTime=if($_.LastWriteTime){{$_.LastWriteTime.ToString('o')}}else{{$null}}}}
}}
");
            BindGrid(_remoteFilesGrid, _remoteFiles);
            WriteAudit("files.list", path, true);
        }
        catch (Exception ex) { WriteAudit("files.list", ex.Message, false); ShowError(ex); }
    }

    private async Task RemoteFileDoubleClickAsync(int rowIndex)
    {
        if (rowIndex < 0 || _remoteFilesGrid.Rows[rowIndex].DataBoundItem is not FileManagerEntry item) return;
        if (item.IsDirectory) { _remotePath.Text = item.FullName; await RefreshRemoteFilesAsync(); }
        else
        {
            try { Process.Start(new ProcessStartInfo(RemotePathToUnc(CurrentHost, item.FullName)) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        }
    }

    private async Task RemotePathUpAsync()
    {
        try
        {
            var current = _remotePath.Text.Trim().TrimEnd('\\');
            if (current.Length <= 2) return;
            var parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(parent)) { _remotePath.Text = parent; await RefreshRemoteFilesAsync(); }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task UploadSelectedToRemoteAsync()
    {
        if (!EnsureConnected() || _localFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        try
        {
            var destinationRemote = Path.Combine(_remotePath.Text.Trim(), item.Name);
            var destinationUnc = RemotePathToUnc(CurrentHost, destinationRemote);
            await Task.Run(() => CopyFileSystemEntry(item.FullName, destinationUnc));
            WriteAudit("files.upload", $"{item.FullName} -> {destinationRemote}", true);
            await RefreshRemoteFilesAsync();
        }
        catch (Exception ex) { WriteAudit("files.upload", ex.Message, false); ShowError(ex); }
    }

    private async Task DownloadSelectedToLocalAsync()
    {
        if (!EnsureConnected() || _remoteFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        try
        {
            var source = RemotePathToUnc(CurrentHost, item.FullName);
            var destination = Path.Combine(_localPath.Text.Trim(), item.Name);
            await Task.Run(() => CopyFileSystemEntry(source, destination));
            WriteAudit("files.download", $"{item.FullName} -> {destination}", true);
            RefreshLocalFiles();
        }
        catch (Exception ex) { WriteAudit("files.download", ex.Message, false); ShowError(ex); }
    }

    private static void CopyFileSystemEntry(string source, string destination)
    {
        if (Directory.Exists(source)) CopyDirectoryRecursive(source, destination);
        else File.Copy(source, destination, true);
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source)) CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private void CreateLocalFolder()
    {
        var name = PromptText("Новая папка", "Имя папки:");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { Directory.CreateDirectory(Path.Combine(_localPath.Text.Trim(), name)); RefreshLocalFiles(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task CreateRemoteFolderAsync()
    {
        if (!EnsureConnected()) return;
        var name = PromptText("Новая папка", "Имя папки:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(_remotePath.Text.Trim(), name);
        try { await _remote.ExecuteTextAsync($"New-Item -ItemType Directory -Path '{PsQuote(path)}' -ErrorAction Stop | Out-Null"); WriteAudit("files.mkdir", path, true); await RefreshRemoteFilesAsync(); }
        catch (Exception ex) { WriteAudit("files.mkdir", ex.Message, false); ShowError(ex); }
    }

    private void RenameLocalEntry()
    {
        if (_localFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        var name = PromptText("Переименование", "Новое имя:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        try
        {
            var dest = Path.Combine(Path.GetDirectoryName(item.FullName)!, name);
            if (item.IsDirectory) Directory.Move(item.FullName, dest); else File.Move(item.FullName, dest);
            RefreshLocalFiles();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RenameRemoteEntryAsync()
    {
        if (!EnsureConnected() || _remoteFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        var name = PromptText("Переименование", "Новое имя:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        try { await _remote.ExecuteTextAsync($"Rename-Item -LiteralPath '{PsQuote(item.FullName)}' -NewName '{PsQuote(name)}' -ErrorAction Stop"); WriteAudit("files.rename", $"{item.FullName} -> {name}", true); await RefreshRemoteFilesAsync(); }
        catch (Exception ex) { WriteAudit("files.rename", ex.Message, false); ShowError(ex); }
    }

    private void DeleteLocalEntry()
    {
        if (_localFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        if (MessageBox.Show($"Удалить {item.FullName}?", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { if (item.IsDirectory) Directory.Delete(item.FullName, true); else File.Delete(item.FullName); RefreshLocalFiles(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteRemoteEntryAsync()
    {
        if (!EnsureConnected() || _remoteFilesGrid.CurrentRow?.DataBoundItem is not FileManagerEntry item) return;
        if (MessageBox.Show($"Удалить {item.FullName}?", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync($"Remove-Item -LiteralPath '{PsQuote(item.FullName)}' -Force{(item.IsDirectory ? " -Recurse" : "")} -ErrorAction Stop"); WriteAudit("files.delete", item.FullName, true); await RefreshRemoteFilesAsync(); }
        catch (Exception ex) { WriteAudit("files.delete", ex.Message, false); ShowError(ex); }
    }

    private void OpenLocalPathInExplorer() => LaunchLocal("explorer.exe", $"\"{_localPath.Text.Trim()}\"");
    private void OpenCurrentRemotePathInExplorer()
    {
        if (!EnsureConnected()) return;
        try { Process.Start(new ProcessStartInfo(RemotePathToUnc(CurrentHost, _remotePath.Text.Trim())) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string RemotePathToUnc(string host, string remotePath)
    {
        var path = remotePath.Trim();
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return $@"\\{host}\{char.ToUpperInvariant(path[0])}$\{path[3..].Replace('/', '\\')}";
        throw new InvalidOperationException("Укажите абсолютный путь, например C:\\Windows.");
    }

    private void RefreshBulkGroups()
    {
        var selected = _bulkFavoriteGroup.SelectedItem?.ToString();
        _bulkFavoriteGroup.Items.Clear();
        foreach (var group in _favorites.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            _bulkFavoriteGroup.Items.Add(group);
        if (_bulkFavoriteGroup.Items.Count > 0) _bulkFavoriteGroup.SelectedItem = _bulkFavoriteGroup.Items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? _bulkFavoriteGroup.Items[0];
    }

    private void LoadBulkFavoriteGroup()
    {
        RefreshBulkGroups();
        var group = _bulkFavoriteGroup.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(group)) return;
        _bulkTargets.Clear();
        foreach (var f in _favorites.Where(x => x.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name))
        {
            var pc = _domainComputers.FirstOrDefault(x => x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase) || x.DnsHostName.Equals(f.Host, StringComparison.OrdinalIgnoreCase));
            _bulkTargets.Add(new BulkTarget { Selected = true, Name = f.Name, Host = f.Host, IpAddress = pc?.IpAddress ?? "", Users = pc?.Users ?? "", WinRmAvailable = pc?.WinRmAvailable ?? false });
        }
        _bulkStatus.Text = $"Группа {group}: {_bulkTargets.Count}";
    }

    private void LoadBulkTargets(bool activeOnly)
    {
        _bulkTargets.Clear();
        foreach (var pc in _domainComputers.Where(x => !activeOnly || x.IsActive).OrderBy(x => x.Name))
            _bulkTargets.Add(new BulkTarget { Selected = activeOnly && pc.WinRmAvailable, Name = pc.Name, Host = string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName, IpAddress = pc.IpAddress, Users = pc.Users, WinRmAvailable = pc.WinRmAvailable });
        _bulkStatus.Text = $"Целей: {_bulkTargets.Count}";
    }

    private void SetAllBulkTargets(bool value)
    {
        _bulkTargetsGrid.EndEdit();
        foreach (var target in _bulkTargets) target.Selected = value;
        _bulkTargets.ResetBindings();
    }

    private async Task RunBulkActionAsync()
    {
        _bulkTargetsGrid.EndEdit();
        var targets = _bulkTargets.Where(x => x.Selected).ToList();
        if (targets.Count == 0) { MessageBox.Show("Не выбраны компьютеры."); return; }
        var (actionName, script, destructive) = GetBulkAction();
        if (string.IsNullOrWhiteSpace(script)) { MessageBox.Show("Введите PowerShell-команду."); return; }
        if (destructive && MessageBox.Show($"Выполнить «{actionName}» на {targets.Count} ПК?", "Массовые действия", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _bulkResults.Clear();
        _bulkCts?.Dispose();
        _bulkCts = new CancellationTokenSource();
        var ct = _bulkCts.Token;
        var done = 0;
        using var semaphore = new SemaphoreSlim((int)_bulkConcurrency.Value, (int)_bulkConcurrency.Value);
        try
        {
            var tasks = targets.Select(async target =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var output = await RemotePowerShellService.ExecuteOneShotTextAsync(target.Host, script, 60000, null, ct);
                    BeginInvoke((Action)(() => { _bulkResults.Add(new BulkResult { Time = DateTime.Now, Host = target.Host, Action = actionName, Success = true, Result = ShortResult(output) }); _bulkStatus.Text = $"Готово {Interlocked.Increment(ref done)} / {targets.Count}"; }));
                    WriteAudit("bulk." + actionName, "OK", true, target.Host);
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() => { _bulkResults.Add(new BulkResult { Time = DateTime.Now, Host = target.Host, Action = actionName, Success = false, Result = ex.Message }); _bulkStatus.Text = $"Готово {Interlocked.Increment(ref done)} / {targets.Count}"; }));
                    WriteAudit("bulk." + actionName, ex.Message, false, target.Host);
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { _bulkStatus.Text = $"Остановлено. Обработано: {done} / {targets.Count}"; }
        finally { _bulkCts?.Dispose(); _bulkCts = null; }
    }

    private (string Name, string Script, bool Destructive) GetBulkAction() => _bulkAction.SelectedIndex switch
    {
        0 => ("gpupdate", "gpupdate /force", false),
        1 => ("restart-spooler", "Restart-Service Spooler -Force -ErrorAction Stop; Get-Service Spooler | Format-Table Name,Status -AutoSize", false),
        2 => ("flush-dns", "ipconfig /flushdns", false),
        3 => ("windows-update-scan", "if(Test-Path $env:SystemRoot\\System32\\UsoClient.exe){Start-Process UsoClient.exe -ArgumentList StartScan; 'StartScan sent'}else{'UsoClient unavailable'}", false),
        4 => ("restart-computer", "Restart-Computer -Force", true),
        5 => ("stop-computer", "Stop-Computer -Force", true),
        6 => ("custom", _bulkCustomScript.Text, true),
        _ => ("custom", _bulkCustomScript.Text, true)
    };

    private static string ShortResult(string text)
    {
        var value = (text ?? "").Trim().Replace("\r", " ").Replace("\n", " | ");
        return value.Length <= 800 ? value : value[..800] + "…";
    }

    private void RememberMacs(string host, IEnumerable<NetworkAdapterInfo> adapters)
    {
        var rows = adapters.Where(x => !string.IsNullOrWhiteSpace(x.MacAddress)).Select(x => new KnownMacInfo
        {
            Host = host, InterfaceAlias = x.InterfaceAlias, MacAddress = NormalizeMac(x.MacAddress), IpAddress = x.IPv4Address, LastSeen = DateTime.Now
        }).ToList();
        if (rows.Count == 0) return;
        _appData.MergeKnownMacs(rows);
        ReloadKnownMacs();
    }

    private void ReloadKnownMacs()
    {
        _knownMacs.RaiseListChangedEvents = false;
        _knownMacs.Clear();
        foreach (var m in _appData.LoadKnownMacs().OrderByDescending(x => x.LastSeen)) _knownMacs.Add(m);
        _knownMacs.RaiseListChangedEvents = true;
        _knownMacs.ResetBindings();
    }

    private async Task SendWakeOnLanAsync()
    {
        try
        {
            var mac = ParseMac(_wolMac.Text);
            var broadcast = IPAddress.Parse(_wolBroadcast.Text.Trim());
            var packet = new byte[6 + 16 * 6];
            for (var i = 0; i < 6; i++) packet[i] = 0xFF;
            for (var i = 6; i < packet.Length; i += 6) Buffer.BlockCopy(mac, 0, packet, i, 6);
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            await udp.SendAsync(packet, packet.Length, new IPEndPoint(broadcast, (int)_wolPort.Value));
            WriteAudit("wol.send", $"MAC={NormalizeMac(_wolMac.Text)} broadcast={broadcast}:{_wolPort.Value}", true, CurrentHost);
            MessageBox.Show("Magic Packet отправлен.", "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { WriteAudit("wol.send", ex.Message, false, CurrentHost); ShowError(ex); }
    }

    private static byte[] ParseMac(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) throw new FormatException("MAC-адрес должен содержать 12 шестнадцатеричных символов.");
        return Enumerable.Range(0, 6).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }

    private static string NormalizeMac(string value)
    {
        var hex = new string((value ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 12 ? string.Join("-", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))) : value;
    }

    private async Task RefreshBitLockerTpmAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_bitLockerGrid, await _remote.ExecuteJsonListAsync<BitLockerVolumeInfo>(@"
if(Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue){
 Get-BitLockerVolume | ForEach-Object {
  [pscustomobject]@{MountPoint=$_.MountPoint;VolumeType=[string]$_.VolumeType;VolumeStatus=[string]$_.VolumeStatus;ProtectionStatus=[string]$_.ProtectionStatus;LockStatus=[string]$_.LockStatus;EncryptionMethod=[string]$_.EncryptionMethod;EncryptionPercentage=[double]$_.EncryptionPercentage;KeyProtectors=(($_.KeyProtector | ForEach-Object {[string]$_.KeyProtectorType}) -join ', ')}
 }
}
"));
            _tpmInfo.Text = await _remote.ExecuteTextAsync(@"
'=== TPM ==='
if(Get-Command Get-Tpm -ErrorAction SilentlyContinue){ Get-Tpm | Format-List TpmPresent,TpmReady,TpmEnabled,TpmActivated,TpmOwned,RestartPending,ManufacturerIdTxt,ManufacturerVersion,ManagedAuthLevel }
else { 'Get-Tpm недоступен.' }
''
'=== TPM WMI ==='
Get-CimInstance -Namespace root/cimv2/security/microsofttpm -ClassName Win32_Tpm -ErrorAction SilentlyContinue | Select-Object ManufacturerIdTxt,ManufacturerVersion,SpecVersion | Format-List
");
            WriteAudit("security.bitlocker-tpm", "Статус BitLocker/TPM", true);
        }
        catch (Exception ex) { WriteAudit("security.bitlocker-tpm", ex.Message, false); ShowError(ex); }
    }

    private async Task SetBitLockerProtectionAsync(bool enable)
    {
        if (!EnsureConnected() || _bitLockerGrid.CurrentRow?.DataBoundItem is not BitLockerVolumeInfo volume || string.IsNullOrWhiteSpace(volume.MountPoint)) return;
        var action = enable ? "возобновить защиту" : "приостановить защиту на одну перезагрузку";
        if (MessageBox.Show($"{action} BitLocker для {volume.MountPoint}?", "BitLocker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var script = enable
                ? $"Resume-BitLocker -MountPoint '{PsQuote(volume.MountPoint)}' -ErrorAction Stop"
                : $"Suspend-BitLocker -MountPoint '{PsQuote(volume.MountPoint)}' -RebootCount 1 -ErrorAction Stop";
            await _remote.ExecuteTextAsync(script);
            WriteAudit(enable ? "bitlocker.resume" : "bitlocker.suspend", volume.MountPoint, true);
            await RefreshBitLockerTpmAsync();
        }
        catch (Exception ex) { WriteAudit(enable ? "bitlocker.resume" : "bitlocker.suspend", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshPrintersAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_printersGrid, await _remote.ExecuteJsonListAsync<PrinterInfoRow>(@"
if(Get-Command Get-Printer -ErrorAction SilentlyContinue){
 Get-Printer | Sort-Object Name | ForEach-Object { [pscustomobject]@{Name=$_.Name;DriverName=$_.DriverName;PortName=$_.PortName;Shared=[bool]$_.Shared;ShareName=$_.ShareName;Type=[string]$_.Type;PrinterStatus=[string]$_.PrinterStatus} }
}else{
 Get-CimInstance Win32_Printer | Sort-Object Name | ForEach-Object { [pscustomobject]@{Name=$_.Name;DriverName=$_.DriverName;PortName=$_.PortName;Shared=[bool]$_.Shared;ShareName=$_.ShareName;Type='';PrinterStatus=[string]$_.PrinterStatus} }
}
"));
            WriteAudit("printers.list", "Список принтеров", true);
        }
        catch (Exception ex) { WriteAudit("printers.list", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshPrintJobsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var printer = _printersGrid.CurrentRow?.DataBoundItem as PrinterInfoRow;
            var script = printer is null
                ? @"
if(Get-Command Get-PrintJob -ErrorAction SilentlyContinue){
 foreach($p in (Get-Printer -ErrorAction SilentlyContinue)){
  Get-PrintJob -PrinterName $p.Name -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{Id=$_.Id;PrinterName=$p.Name;DocumentName=$_.DocumentName;UserName=$_.UserName;JobStatus=[string]$_.JobStatus;PagesPrinted=$_.PagesPrinted;TotalPages=$_.TotalPages;Size=$_.Size} }
 }
}
"
                : $@"
if(Get-Command Get-PrintJob -ErrorAction SilentlyContinue){{
 Get-PrintJob -PrinterName '{PsQuote(printer.Name)}' -ErrorAction SilentlyContinue | ForEach-Object {{ [pscustomobject]@{{Id=$_.Id;PrinterName='{PsQuote(printer.Name)}';DocumentName=$_.DocumentName;UserName=$_.UserName;JobStatus=[string]$_.JobStatus;PagesPrinted=$_.PagesPrinted;TotalPages=$_.TotalPages;Size=$_.Size}} }}
}}
";
            BindGrid(_printJobsGrid, await _remote.ExecuteJsonListAsync<PrintJobInfoRow>(script));
            WriteAudit("printers.jobs", printer?.Name ?? "Все принтеры", true);
        }
        catch (Exception ex) { WriteAudit("printers.jobs", ex.Message, false); ShowError(ex); }
    }

    private async Task CancelPrintJobAsync()
    {
        if (!EnsureConnected() || _printJobsGrid.CurrentRow?.DataBoundItem is not PrintJobInfoRow job) return;
        if (MessageBox.Show($"Отменить задание #{job.Id} «{job.DocumentName}» на {job.PrinterName}?", "Печать", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _remote.ExecuteTextAsync($"Remove-PrintJob -PrinterName '{PsQuote(job.PrinterName)}' -ID {job.Id} -ErrorAction Stop"); WriteAudit("printers.cancel-job", $"{job.PrinterName} #{job.Id}", true); await RefreshPrintJobsAsync(); }
        catch (Exception ex) { WriteAudit("printers.cancel-job", ex.Message, false); ShowError(ex); }
    }

    private async Task RestartSpoolerAsync()
    {
        if (!EnsureConnected()) return;
        try { await _remote.ExecuteTextAsync("Restart-Service Spooler -Force -ErrorAction Stop"); WriteAudit("printers.restart-spooler", "Spooler", true); await RefreshPrintersAsync(); }
        catch (Exception ex) { WriteAudit("printers.restart-spooler", ex.Message, false); ShowError(ex); }
    }

    private static string? PromptText(string title, string label, string initial = "")
    {
        using var form = new Form { Text = title, Width = 480, Height = 160, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var lbl = new Label { Text = label, Left = 12, Top = 15, Width = 430 };
        var box = new TextBox { Left = 12, Top = 38, Width = 440, Text = initial };
        var ok = new Button { Text = "OK", Left = 276, Top = 75, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", Left = 366, Top = 75, Width = 86, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([lbl, box, ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
