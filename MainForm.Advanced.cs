using DomainAdminConsole.Models;
using DomainAdminConsole.Services;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace DomainAdminConsole;

public sealed partial class MainForm
{
    private readonly AppDataStore _appData = new();
    private readonly SortableBindingList<FavoriteComputer> _favorites = [];

    private readonly DataGridView _favoritesGrid = Grid();
    private readonly ComboBox _favoriteGroupFilter = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView _shadowGrid = Grid();
    private readonly CheckBox _shadowControl = new() { Text = "Управление", Checked = true, AutoSize = true };
    private readonly CheckBox _shadowNoConsent = new() { Text = "Без запроса согласия", Checked = false, AutoSize = true };

    private readonly TextBox _registryPath = new() { Width = 500, Text = @"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" };
    private readonly TextBox _registryValueName = new() { Width = 190, PlaceholderText = "Имя значения" };
    private readonly TextBox _registryValueData = new() { Width = 250, PlaceholderText = "Данные" };
    private readonly ComboBox _registryValueType = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _registryGrid = Grid();
    private readonly TreeView _registryTree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true, ShowPlusMinus = true };
    private bool _registryTreeInitialized;
    private bool _registryTreeSelectionSync;

    private readonly DataGridView _taskGrid = Grid();
    private readonly DataGridView _appsGrid = Grid();
    private readonly TextBox _appFilter = new() { Width = 280, PlaceholderText = "Фильтр программ" };
    private List<InstalledAppInfo> _installedApps = [];

    private readonly DataGridView _accountsGrid = Grid();
    private readonly DataGridView _adminMembersGrid = Grid();
    private readonly TextBox _adminPrincipal = new() { Width = 280, PlaceholderText = @"DOMAIN\user или localuser" };

    private readonly DataGridView _adapterGrid = Grid();
    private readonly DataGridView _routeGrid = Grid();

    private readonly DataGridView _hotfixGrid = Grid();
    private readonly DataGridView _pendingUpdateGrid = Grid();
    private readonly RichTextBox _updateStatus = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };

    private readonly DataGridView _auditGrid = Grid();

    private void InitializeAdvancedState()
    {
        foreach (var favorite in _appData.LoadFavorites())
            _favorites.Add(favorite);

        _favoritesGrid.DataSource = _favorites;
        RefreshFavoriteGroups();
        ConfigureDomainContextMenu();
        RefreshAuditGrid();
    }

    private TabPage BuildFavoritesTab()
    {
        var tab = new TabPage("Избранное");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        _favoriteGroupFilter.Items.Add("Все группы");
        _favoriteGroupFilter.SelectedIndex = 0;
        _favoriteGroupFilter.SelectedIndexChanged += (_, _) => ApplyFavoriteFilter();

        bar.Controls.Add(new Label { Text = "Группа:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_favoriteGroupFilter);
        bar.Controls.Add(Button("Добавить текущий", (_, _) => AddCurrentFavorite(), 135));
        bar.Controls.Add(Button("Изменить", (_, _) => EditSelectedFavorite()));
        bar.Controls.Add(Button("Удалить", (_, _) => RemoveSelectedFavorite()));
        bar.Controls.Add(Button("Подключиться", async (_, _) => await ConnectSelectedFavoriteAsync(), 115));
        bar.Controls.Add(Button("RDP", (_, _) => RdpSelectedFavorite(), 80));

        _favoritesGrid.AutoGenerateColumns = false;
        _favoritesGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FavoriteComputer.Name), HeaderText = "Имя", Width = 160 });
        _favoritesGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FavoriteComputer.Host), HeaderText = "DNS / IP", Width = 200 });
        _favoritesGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FavoriteComputer.Group), HeaderText = "Группа", Width = 140 });
        _favoritesGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FavoriteComputer.Notes), HeaderText = "Примечание", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _favoritesGrid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ConnectSelectedFavoriteAsync(); };
        MirrorToolbarToGridContextMenu(_favoritesGrid, bar);

        tab.Controls.Add(_favoritesGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildShadowTab()
    {
        var tab = new TabPage("RDP Shadow");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Сеансы", async (_, _) => await RefreshShadowSessionsAsync()));
        bar.Controls.Add(_shadowControl);
        bar.Controls.Add(_shadowNoConsent);
        bar.Controls.Add(Button("Подключиться Shadow", (_, _) => LaunchShadow(), 155));
        bar.Controls.Add(Button("Обычный RDP", (_, _) => { if (!string.IsNullOrWhiteSpace(CurrentHost)) LaunchLocal("mstsc.exe", $"/v:{CurrentHost}"); }, 120));

        MirrorToolbarToGridContextMenu(_shadowGrid, bar);
        tab.Controls.Add(_shadowGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildRegistryTab()
    {
        var tab = new TabPage("Реестр") { Padding = new Padding(6) };
        _registryValueType.Items.AddRange(["String", "ExpandString", "MultiString", "DWord", "QWord", "Binary"]);
        if (_registryValueType.SelectedIndex < 0) _registryValueType.SelectedIndex = 0;

        _registryTree.BeforeExpand += async (_, e) => await LoadRegistryTreeChildrenAsync(e.Node);
        _registryTree.AfterSelect += async (_, e) =>
        {
            if (_registryTreeSelectionSync || e.Node.Tag is not string path) return;
            _registryPath.Text = path;
            await ReadRegistryAsync();
        };
        _registryTree.NodeMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) _registryTree.SelectedNode = e.Node; };
        var treeMenu = new ContextMenuStrip();
        treeMenu.Items.Add("Обновить ветку", null, async (_, _) =>
        {
            if (_registryTree.SelectedNode is { } node)
            {
                node.Nodes.Clear();
                node.Nodes.Add(new TreeNode("Загрузка...") { Tag = null });
                await LoadRegistryTreeChildrenAsync(node);
            }
        });
        treeMenu.Items.Add("Копировать путь", null, (_, _) =>
        {
            if (_registryTree.SelectedNode?.Tag is string path) try { Clipboard.SetText(path); } catch { }
        });
        _registryTree.ContextMenuStrip = treeMenu;

        _registryGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _registryGrid.Rows[e.RowIndex].DataBoundItem is not RegistryValueRow row) return;
            _registryValueName.Text = row.Name == "(Default)" ? string.Empty : row.Name;
            _registryValueData.Text = row.Value;
            var type = _registryValueType.Items.Cast<string>().FirstOrDefault(x => x.Equals(row.Type, StringComparison.OrdinalIgnoreCase));
            if (type is not null) _registryValueType.SelectedItem = type;
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var pathBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), WrapContents = false };
        _registryPath.Width = 560;
        pathBar.Controls.Add(_registryPath);
        pathBar.Controls.Add(Button("Обновить", async (_, _) => await ReadRegistryAsync(), 100));
        pathBar.Controls.Add(Button("Создать ключ", async (_, _) => await CreateRegistryKeyAsync(), 120));

        var content = CreateSafeSplitContainer(Orientation.Vertical, desiredDistance: 285, panel1MinSize: 190, panel2MinSize: 360);
        var treeBox = new GroupBox { Text = "Разделы реестра", Dock = DockStyle.Fill, Padding = new Padding(6) };
        treeBox.Controls.Add(_registryTree);
        var valuesBox = new GroupBox { Text = "Значения выбранного ключа", Dock = DockStyle.Fill, Padding = new Padding(6) };
        valuesBox.Controls.Add(_registryGrid);
        content.Panel1.Controls.Add(treeBox);
        content.Panel2.Controls.Add(valuesBox);

        var editBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), WrapContents = false };
        editBar.Controls.Add(_registryValueName);
        editBar.Controls.Add(_registryValueData);
        editBar.Controls.Add(_registryValueType);
        editBar.Controls.Add(Button("Записать", async (_, _) => await SetRegistryValueAsync()));
        editBar.Controls.Add(Button("Удалить значение", async (_, _) => await RemoveRegistryValueAsync(), 145));

        MirrorToolbarToGridContextMenu(_registryGrid, pathBar);
        MirrorAdditionalToolbarButtonsToGridContextMenu(_registryGrid, editBar);
        root.Controls.Add(pathBar, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(editBar, 0, 2);
        tab.Controls.Add(root);

        InitializeRegistryTree();
        return tab;
    }

    private void InitializeRegistryTree()
    {
        if (_registryTreeInitialized) return;
        _registryTreeInitialized = true;
        _registryTree.Nodes.Clear();
        AddRegistryRoot("HKEY_LOCAL_MACHINE", @"Registry::HKEY_LOCAL_MACHINE");
        AddRegistryRoot("HKEY_CURRENT_USER", @"Registry::HKEY_CURRENT_USER");
        AddRegistryRoot("HKEY_CLASSES_ROOT", @"Registry::HKEY_CLASSES_ROOT");
        AddRegistryRoot("HKEY_USERS", @"Registry::HKEY_USERS");
        AddRegistryRoot("HKEY_CURRENT_CONFIG", @"Registry::HKEY_CURRENT_CONFIG");
    }

    private void AddRegistryRoot(string caption, string path)
    {
        var node = new TreeNode(caption) { Tag = path };
        node.Nodes.Add(new TreeNode("Загрузка...") { Tag = null });
        _registryTree.Nodes.Add(node);
    }

    private async Task LoadRegistryTreeChildrenAsync(TreeNode node)
    {
        if (!EnsureConnected() || node.Tag is not string path) return;
        if (node.Nodes.Count > 0 && node.Nodes.Cast<TreeNode>().All(x => x.Tag is string)) return;
        try
        {
            var rows = await _remote.ExecuteJsonListAsync<RegistryTreeNodeRow>($@"
Get-ChildItem -LiteralPath '{PsQuote(path)}' -ErrorAction Stop | Sort-Object PSChildName | ForEach-Object {{
 [pscustomobject]@{{Name=$_.PSChildName;Path=$_.PSPath}}
}}
");
            node.Nodes.Clear();
            foreach (var row in rows)
            {
                var child = new TreeNode(row.Name) { Tag = row.Path };
                child.Nodes.Add(new TreeNode("Загрузка...") { Tag = null });
                node.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode("Недоступно") { ForeColor = Color.Firebrick });
            WriteAudit("registry.tree", $"{path}: {ex.Message}", false);
        }
    }

    private async Task RefreshSelectedRegistryTreeNodeAsync()
    {
        if (_registryTree.SelectedNode is not { } node) return;
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("Загрузка...") { Tag = null });
        await LoadRegistryTreeChildrenAsync(node);
    }

    private TabPage BuildScheduledTasksTab()
    {
        var tab = new TabPage("Планировщик");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshScheduledTasksAsync()));
        bar.Controls.Add(Button("Запустить", async (_, _) => await TaskActionAsync("start")));
        bar.Controls.Add(Button("Остановить", async (_, _) => await TaskActionAsync("stop")));
        bar.Controls.Add(Button("Включить", async (_, _) => await TaskActionAsync("enable")));
        bar.Controls.Add(Button("Отключить", async (_, _) => await TaskActionAsync("disable")));
        MirrorToolbarToGridContextMenu(_taskGrid, bar);
        tab.Controls.Add(_taskGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildInstalledAppsTab()
    {
        var tab = new TabPage("Программы");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshInstalledAppsAsync()));
        bar.Controls.Add(_appFilter);
        bar.Controls.Add(Button("Удалить MSI", async (_, _) => await UninstallSelectedMsiAsync(), 110));
        _appFilter.TextChanged += (_, _) => ApplyAppFilter();
        MirrorToolbarToGridContextMenu(_appsGrid, bar);
        tab.Controls.Add(_appsGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildLocalAccountsTab()
    {
        var tab = new TabPage("Локальные учётки");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshLocalAccountsAsync()));
        bar.Controls.Add(_adminPrincipal);
        bar.Controls.Add(Button("В администраторы", async (_, _) => await ChangeLocalAdminMembershipAsync(true), 130));
        bar.Controls.Add(Button("Убрать из админов", async (_, _) => await ChangeLocalAdminMembershipAsync(false), 140));
        bar.Controls.Add(Button("Включить учётку", async (_, _) => await ToggleSelectedLocalUserAsync(true), 125));
        bar.Controls.Add(Button("Отключить учётку", async (_, _) => await ToggleSelectedLocalUserAsync(false), 135));
        MirrorToolbarToGridContextMenu(_accountsGrid, bar);
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 360);
        var usersPanel = new Panel { Dock = DockStyle.Fill };
        usersPanel.Controls.Add(_accountsGrid);
        usersPanel.Controls.Add(bar);
        split.Panel1.Controls.Add(usersPanel);
        split.Panel2.Controls.Add(_adminMembersGrid);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildNetworkConfigTab()
    {
        var tab = new TabPage("Сеть ПК");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 330);

        var adapterPanel = new Panel { Dock = DockStyle.Fill };
        var adapterBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        adapterBar.Controls.Add(Button("Обновить", async (_, _) => await RefreshNetworkConfigurationAsync()));
        adapterBar.Controls.Add(Button("Flush DNS", async (_, _) => await RunNativeUtilityAsync("ipconfig.exe", "/flushdns", "ipconfig /flushdns"), 105));
        adapterBar.Controls.Add(Button("Register DNS", async (_, _) => await RunNativeUtilityAsync("ipconfig.exe", "/registerdns", "ipconfig /registerdns"), 115));
        MirrorToolbarToGridContextMenu(_adapterGrid, adapterBar);
        adapterPanel.Controls.Add(_adapterGrid);
        adapterPanel.Controls.Add(adapterBar);

        var routePanel = new Panel { Dock = DockStyle.Fill };
        routePanel.Controls.Add(_routeGrid);
        split.Panel1.Controls.Add(adapterPanel);
        split.Panel2.Controls.Add(routePanel);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildWindowsUpdateTab()
    {
        var tab = new TabPage("Windows Update");
        var split = CreateSafeSplitContainer(Orientation.Horizontal, desiredDistance: 280);

        var top = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Диагностика", async (_, _) => await RefreshWindowsUpdateAsync(), 110));
        bar.Controls.Add(Button("Искать обновления", async (_, _) => await SearchPendingUpdatesAsync(), 135));
        bar.Controls.Add(Button("Запустить сканирование", async (_, _) => await TriggerWindowsUpdateAsync("StartScan"), 150));
        bar.Controls.Add(Button("Запустить установку", async (_, _) => await TriggerWindowsUpdateAsync("StartInstall"), 140));
        MirrorToolbarToGridContextMenu(_hotfixGrid, bar);
        top.Controls.Add(_hotfixGrid);
        top.Controls.Add(bar);

        var bottom = CreateSafeSplitContainer(Orientation.Vertical, desiredDistance: 520);
        bottom.Panel1.Controls.Add(_pendingUpdateGrid);
        bottom.Panel2.Controls.Add(_updateStatus);

        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        tab.Controls.Add(split);
        return tab;
    }

    private TabPage BuildAuditTab()
    {
        var tab = new TabPage("Журнал действий");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", (_, _) => RefreshAuditGrid()));
        bar.Controls.Add(Button("Открыть папку", (_, _) => LaunchLocal("explorer.exe", _appData.DataDirectory), 115));
        bar.Controls.Add(Button("Очистить", (_, _) => ClearAudit(), 90));
        MirrorToolbarToGridContextMenu(_auditGrid, bar);
        tab.Controls.Add(_auditGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private void ConfigureDomainContextMenu()
    {
        var menu = _domainGrid.ContextMenuStrip ?? CreateGridContextMenu(_domainGrid);

        var refreshDomainItem = new ToolStripMenuItem("Обновить домен", GetSystemActionImage("Обновить"));
        refreshDomainItem.Click += async (_, _) => await LoadDomainAsync();
        var stopDomainItem = new ToolStripMenuItem("Стоп", GetSystemActionImage("Стоп"));
        stopDomainItem.Click += (_, _) => _domainScanCts?.Cancel();

        var connectItem = new ToolStripMenuItem("Подключиться", GetSystemActionImage("Подключиться"));
        connectItem.Click += async (_, _) =>
        {
            if (_domainGrid.CurrentRow?.DataBoundItem is not DomainComputer pc) return;
            var host = string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName;
            _target.Text = host;
            await ConnectAsync(host);
        };

        var favoriteItem = new ToolStripMenuItem("Добавить в избранное", GetSystemActionImage("Добавить"));
        favoriteItem.Click += (_, _) => AddDomainComputerToFavorites();

        var rdpItem = new ToolStripMenuItem("RDP", GetSystemActionImage("RDP"));
        rdpItem.Click += (_, _) =>
        {
            if (_domainGrid.CurrentRow?.DataBoundItem is DomainComputer pc)
                LaunchLocal("mstsc.exe", $"/v:{(string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName)}");
        };

        var shareItem = new ToolStripMenuItem("Открыть C$", GetSystemActionImage("Открыть"));
        shareItem.Click += (_, _) =>
        {
            if (_domainGrid.CurrentRow?.DataBoundItem is DomainComputer pc)
                LaunchLocal("explorer.exe", $@"\\{(string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName)}\c$");
        };

        menu.Items.Insert(0, new ToolStripSeparator());
        menu.Items.Insert(0, stopDomainItem);
        menu.Items.Insert(0, refreshDomainItem);
        menu.Items.Insert(0, new ToolStripSeparator());
        menu.Items.Insert(0, shareItem);
        menu.Items.Insert(0, rdpItem);
        menu.Items.Insert(0, favoriteItem);
        menu.Items.Insert(0, connectItem);
        _domainGrid.ContextMenuStrip = menu;
    }


    private void PopulateMainFavoritesMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var addCurrent = new ToolStripMenuItem("Добавить текущий ПК", UiIconFactory.Get(UiIconKind.Star, 16));
        addCurrent.Click += (_, _) => AddCurrentFavorite();
        parent.DropDownItems.Add(addCurrent);

        var addSelected = new ToolStripMenuItem("Добавить выбранный ПК из домена", UiIconFactory.Get(UiIconKind.Add, 16))
        {
            Enabled = _domainGrid.CurrentRow?.DataBoundItem is DomainComputer
        };
        addSelected.Click += (_, _) => AddDomainComputerToFavorites();
        parent.DropDownItems.Add(addSelected);
        parent.DropDownItems.Add(new ToolStripSeparator());

        if (_favorites.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("Нет избранных ПК") { Enabled = false });
            return;
        }

        foreach (var group in _favorites.GroupBy(x => string.IsNullOrWhiteSpace(x.Group) ? "Общие" : x.Group)
                     .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var groupItem = new ToolStripMenuItem(group.Key, UiIconFactory.Get(UiIconKind.FolderOpen, 16));
            foreach (var favorite in group.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var favoriteItem = new ToolStripMenuItem(favorite.Name, UiIconFactory.Get(UiIconKind.Star, 16))
                {
                    ToolTipText = favorite.Host
                };

                var connect = new ToolStripMenuItem("Подключиться", UiIconFactory.Get(UiIconKind.Connect, 16));
                connect.Click += async (_, _) => await ConnectFavoriteAsync(favorite);
                favoriteItem.DropDownItems.Add(connect);

                var rdp = new ToolStripMenuItem("RDP", UiIconFactory.Get(UiIconKind.Rdp, 16));
                rdp.Click += (_, _) => LaunchLocal("mstsc.exe", $"/v:{favorite.Host}");
                favoriteItem.DropDownItems.Add(rdp);

                var share = new ToolStripMenuItem("Открыть C$", UiIconFactory.Get(UiIconKind.FolderOpen, 16));
                share.Click += (_, _) => LaunchLocal("explorer.exe", $@"\\{favorite.Host}\c$");
                favoriteItem.DropDownItems.Add(share);

                favoriteItem.DropDownItems.Add(new ToolStripSeparator());

                var edit = new ToolStripMenuItem("Изменить", UiIconFactory.Get(UiIconKind.Edit, 16));
                edit.Click += (_, _) => EditFavorite(favorite);
                favoriteItem.DropDownItems.Add(edit);

                var remove = new ToolStripMenuItem("Удалить", UiIconFactory.Get(UiIconKind.Delete, 16));
                remove.Click += (_, _) => RemoveFavorite(favorite);
                favoriteItem.DropDownItems.Add(remove);

                groupItem.DropDownItems.Add(favoriteItem);
            }
            parent.DropDownItems.Add(groupItem);
        }
    }

    private async Task ConnectFavoriteAsync(FavoriteComputer favorite)
    {
        _target.Text = favorite.Host;
        await ConnectAsync(favorite.Host);
    }

    private void EditFavorite(FavoriteComputer selected)
    {
        using var dialog = new FavoriteEditDialog(new FavoriteComputer
        {
            Name = selected.Name,
            Host = selected.Host,
            Group = selected.Group,
            Notes = selected.Notes
        });
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var duplicate = _favorites.Any(x => !ReferenceEquals(x, selected) &&
            x.Host.Equals(dialog.Result.Host, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            MessageBox.Show("ПК с таким DNS/IP уже есть в избранном.", "Избранное", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        selected.Name = dialog.Result.Name;
        selected.Host = dialog.Result.Host;
        selected.Group = dialog.Result.Group;
        selected.Notes = dialog.Result.Notes;
        _favorites.ResetBindings();
        SaveFavorites();
        WriteAudit("favorites.edit", $"{selected.Group}: {selected.Name} ({selected.Host})", true, selected.Host);
    }

    private void RemoveFavorite(FavoriteComputer selected)
    {
        if (MessageBox.Show($"Удалить {selected.Name} из избранного?", "Избранное",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _favorites.Remove(selected);
        SaveFavorites();
        WriteAudit("favorites.remove", $"{selected.Name} ({selected.Host})", true, selected.Host);
    }

    private void ConfigureAdvancedTray(ContextMenuStrip menu)
    {
        var favoriteMenu = new ToolStripMenuItem("Избранные ПК");
        menu.Items.Insert(Math.Max(0, menu.Items.Count - 2), favoriteMenu);
        PopulateTrayFavorites(favoriteMenu);
    }

    private void PopulateTrayFavorites(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        foreach (var group in _favorites.GroupBy(x => x.Group).OrderBy(x => x.Key))
        {
            var groupItem = new ToolStripMenuItem(group.Key);
            foreach (var favorite in group.OrderBy(x => x.Name))
            {
                var item = new ToolStripMenuItem(favorite.Name) { ToolTipText = favorite.Host };
                item.Click += async (_, _) =>
                {
                    RestoreFromTray();
                    _target.Text = favorite.Host;
                    await ConnectAsync(favorite.Host);
                };
                groupItem.DropDownItems.Add(item);
            }
            parent.DropDownItems.Add(groupItem);
        }
        if (parent.DropDownItems.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem("Нет избранных") { Enabled = false });
    }

    private void RefreshTrayFavoriteMenu()
    {
        if (_tray.ContextMenuStrip is null) return;
        var item = _tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().FirstOrDefault(x => x.Text == "Избранные ПК");
        if (item is not null) PopulateTrayFavorites(item);
    }

    private void AddDomainComputerToFavorites()
    {
        if (_domainGrid.CurrentRow?.DataBoundItem is not DomainComputer pc) return;
        var host = string.IsNullOrWhiteSpace(pc.DnsHostName) ? pc.Name : pc.DnsHostName;
        AddFavorite(new FavoriteComputer { Name = pc.Name, Host = host, Group = "Доменные ПК", Notes = pc.Description });
    }

    private void AddCurrentFavorite()
    {
        var host = CurrentHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show("Сначала укажите или подключите ПК.", "Избранное", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AddFavorite(new FavoriteComputer { Name = host.Split('.')[0], Host = host, Group = "Общие" });
    }

    private void AddFavorite(FavoriteComputer initial)
    {
        using var dialog = new FavoriteEditDialog(initial);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (_favorites.Any(x => x.Host.Equals(dialog.Result.Host, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Этот ПК уже есть в избранном.", "Избранное", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _favorites.Add(dialog.Result);
        SaveFavorites();
        WriteAudit("favorites.add", $"{dialog.Result.Group}: {dialog.Result.Name} ({dialog.Result.Host})", true, dialog.Result.Host);
    }

    private void EditSelectedFavorite()
    {
        if (_favoritesGrid.CurrentRow?.DataBoundItem is FavoriteComputer selected)
            EditFavorite(selected);
    }

    private void RemoveSelectedFavorite()
    {
        if (_favoritesGrid.CurrentRow?.DataBoundItem is FavoriteComputer selected)
            RemoveFavorite(selected);
    }

    private async Task ConnectSelectedFavoriteAsync()
    {
        if (_favoritesGrid.CurrentRow?.DataBoundItem is not FavoriteComputer selected) return;
        _target.Text = selected.Host;
        await ConnectAsync(selected.Host);
    }

    private void RdpSelectedFavorite()
    {
        if (_favoritesGrid.CurrentRow?.DataBoundItem is FavoriteComputer selected)
            LaunchLocal("mstsc.exe", $"/v:{selected.Host}");
    }

    private void SaveFavorites()
    {
        _appData.SaveFavorites(_favorites);
        RefreshFavoriteGroups();
        RefreshTrayFavoriteMenu();
    }

    private void RefreshFavoriteGroups()
    {
        var current = _favoriteGroupFilter.SelectedItem?.ToString() ?? "Все группы";
        _favoriteGroupFilter.Items.Clear();
        _favoriteGroupFilter.Items.Add("Все группы");
        foreach (var group in _favorites.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            _favoriteGroupFilter.Items.Add(group);
        _favoriteGroupFilter.SelectedItem = _favoriteGroupFilter.Items.Cast<string>().FirstOrDefault(x => x.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "Все группы";
    }

    private void ApplyFavoriteFilter()
    {
        var group = _favoriteGroupFilter.SelectedItem?.ToString();
        _favoritesGrid.DataSource = string.IsNullOrWhiteSpace(group) || group == "Все группы"
            ? _favorites
            : new SortableBindingList<FavoriteComputer>(_favorites.Where(x => x.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private async Task RefreshShadowSessionsAsync()
    {
        var host = CurrentHost;
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            BindGrid(_shadowGrid, await RdpSessionService.GetSessionsAsync(host));
            WriteAudit("rdp.sessions", "Получен список терминальных сеансов", true);
        }
        catch (Exception ex)
        {
            WriteAudit("rdp.sessions", ex.Message, false);
            ShowError(ex);
        }
    }

    private void LaunchShadow()
    {
        if (_shadowGrid.CurrentRow?.DataBoundItem is not RdpSessionInfo session) return;
        var args = $"/v:{CurrentHost} /shadow:{session.Id}";
        if (_shadowControl.Checked) args += " /control";
        if (_shadowNoConsent.Checked) args += " /noConsentPrompt";
        WriteAudit("rdp.shadow", $"SessionId={session.Id}; User={session.UserDisplay}; Control={_shadowControl.Checked}; NoConsent={_shadowNoConsent.Checked}", true);
        LaunchLocal("mstsc.exe", args);
    }

    private async Task ReadRegistryAsync()
    {
        if (!EnsureConnected()) return;
        var path = _registryPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            BindGrid(_registryGrid, await _remote.ExecuteJsonListAsync<RegistryValueRow>($@"
$key = Get-Item -LiteralPath '{PsQuote(path)}' -ErrorAction Stop
foreach($name in $key.GetValueNames()){{
 try {{
  $kind=$key.GetValueKind($name).ToString()
  $v=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
  if($v -is [byte[]]){{$text=($v | ForEach-Object {{$_.ToString('X2')}}) -join ' '}} elseif($v -is [array]){{$text=$v -join '; '}} else {{$text=[string]$v}}
  [pscustomobject]@{{Name=if($name){{$name}}else{{'(Default)'}};Type=$kind;Value=$text;IsKey=$false}}
 }} catch {{}}
}}
"));
            if (_registryGrid.Columns[nameof(RegistryValueRow.IsKey)] is { } isKeyColumn) isKeyColumn.Visible = false;
            WriteAudit("registry.read", path, true);
        }
        catch (Exception ex) { WriteAudit("registry.read", $"{path}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task CreateRegistryKeyAsync()
    {
        if (!EnsureConnected()) return;
        var parent = _registryPath.Text.Trim().TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(parent)) return;
        var name = PromptText("Создать раздел реестра", "Имя нового раздела:", string.Empty);
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = parent + "\\" + name.Trim();
        try
        {
            await _remote.ExecuteTextAsync($"New-Item -Path '{PsQuote(path)}' -Force -ErrorAction Stop | Out-Null");
            WriteAudit("registry.create-key", path, true);
            await RefreshSelectedRegistryTreeNodeAsync();
        }
        catch (Exception ex) { WriteAudit("registry.create-key", $"{path}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task SetRegistryValueAsync()
    {
        if (!EnsureConnected()) return;
        var path = _registryPath.Text.Trim();
        var name = _registryValueName.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) return;
        var type = _registryValueType.Text;
        var data = _registryValueData.Text;

        string valueExpression = type switch
        {
            "DWord" => $"[int]'{PsQuote(data)}'",
            "QWord" => $"[long]'{PsQuote(data)}'",
            "MultiString" => $"@('{string.Join("','", data.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(PsQuote))}')",
            "Binary" => $"[byte[]]@({string.Join(',', data.Split(new[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(x => $"0x{x.Replace("0x", "", StringComparison.OrdinalIgnoreCase)}"))})",
            _ => $"'{PsQuote(data)}'"
        };

        try
        {
            await _remote.ExecuteTextAsync($"New-Item -Path '{PsQuote(path)}' -Force | Out-Null; New-ItemProperty -LiteralPath '{PsQuote(path)}' -Name '{PsQuote(name)}' -PropertyType {type} -Value {valueExpression} -Force -ErrorAction Stop | Out-Null");
            WriteAudit("registry.set", $"{path}\\{name}; Type={type}", true);
            await ReadRegistryAsync();
        }
        catch (Exception ex) { WriteAudit("registry.set", $"{path}\\{name}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RemoveRegistryValueAsync()
    {
        if (!EnsureConnected()) return;
        var path = _registryPath.Text.Trim();
        var name = _registryValueName.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) return;
        if (MessageBox.Show($"Удалить значение {name}?", "Реестр", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _remote.ExecuteTextAsync($"Remove-ItemProperty -LiteralPath '{PsQuote(path)}' -Name '{PsQuote(name)}' -ErrorAction Stop");
            WriteAudit("registry.remove", $"{path}\\{name}", true);
            await ReadRegistryAsync();
        }
        catch (Exception ex) { WriteAudit("registry.remove", $"{path}\\{name}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshScheduledTasksAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_taskGrid, await _remote.ExecuteJsonListAsync<ScheduledTaskInfo>(@"
Get-ScheduledTask | Sort-Object TaskPath,TaskName | ForEach-Object {
 [pscustomobject]@{ TaskName=$_.TaskName; TaskPath=$_.TaskPath; State=$_.State.ToString(); Author=$_.Author }
}
"));
            WriteAudit("tasks.list", "Получен список заданий", true);
        }
        catch (Exception ex) { WriteAudit("tasks.list", ex.Message, false); ShowError(ex); }
    }

    private async Task TaskActionAsync(string action)
    {
        if (!EnsureConnected() || _taskGrid.CurrentRow?.DataBoundItem is not ScheduledTaskInfo task) return;
        var command = action switch
        {
            "start" => "Start-ScheduledTask",
            "stop" => "Stop-ScheduledTask",
            "enable" => "Enable-ScheduledTask",
            "disable" => "Disable-ScheduledTask",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        try
        {
            await _remote.ExecuteTextAsync($"{command} -TaskName '{PsQuote(task.TaskName)}' -TaskPath '{PsQuote(task.TaskPath)}' -ErrorAction Stop");
            WriteAudit($"tasks.{action}", $"{task.TaskPath}{task.TaskName}", true);
            await RefreshScheduledTasksAsync();
        }
        catch (Exception ex) { WriteAudit($"tasks.{action}", $"{task.TaskName}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshInstalledAppsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _installedApps = await _remote.ExecuteJsonListAsync<InstalledAppInfo>(@"
$items=@()
$paths=@(
 @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'; Arch='x64'},
 @{Path='HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'; Arch='x86'}
)
foreach($x in $paths){
 Get-ItemProperty $x.Path -ErrorAction SilentlyContinue | Where-Object DisplayName | ForEach-Object {
  $items += [pscustomobject]@{DisplayName=$_.DisplayName;DisplayVersion=$_.DisplayVersion;Publisher=$_.Publisher;InstallDate=$_.InstallDate;Architecture=$x.Arch;UninstallString=$_.UninstallString}
 }
}
$items | Sort-Object DisplayName,DisplayVersion -Unique
");
            ApplyAppFilter();
            WriteAudit("apps.list", $"Найдено: {_installedApps.Count}", true);
        }
        catch (Exception ex) { WriteAudit("apps.list", ex.Message, false); ShowError(ex); }
    }

    private void ApplyAppFilter()
    {
        var f = _appFilter.Text.Trim();
        BindGrid(_appsGrid, _installedApps.Where(x => string.IsNullOrWhiteSpace(f)
            || x.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || x.Publisher.Contains(f, StringComparison.OrdinalIgnoreCase)
            || x.DisplayVersion.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList());
        if (_appsGrid.Columns[nameof(InstalledAppInfo.UninstallString)] is { } col) col.Visible = false;
    }

    private async Task UninstallSelectedMsiAsync()
    {
        if (!EnsureConnected() || _appsGrid.CurrentRow?.DataBoundItem is not InstalledAppInfo app) return;
        var match = Regex.Match(app.UninstallString ?? "", @"\{[0-9A-Fa-f\-]{36}\}");
        if (!match.Success)
        {
            MessageBox.Show("Для выбранной программы не найден MSI ProductCode. Автоматическое удаление в этой версии выполняется только для MSI.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"Удалить {app.DisplayName} {app.DisplayVersion}?\r\n\r\nКоманда будет выполнена тихо через msiexec /x.", "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await _remote.ExecuteTextAsync($"Start-Process msiexec.exe -ArgumentList '/x {match.Value} /qn /norestart' -Wait -ErrorAction Stop");
            WriteAudit("apps.uninstall-msi", $"{app.DisplayName} {app.DisplayVersion}; {match.Value}", true);
            await RefreshInstalledAppsAsync();
        }
        catch (Exception ex) { WriteAudit("apps.uninstall-msi", $"{app.DisplayName}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshLocalAccountsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_accountsGrid, await _remote.ExecuteJsonListAsync<LocalAccountInfo>(@"
$adminGroup=(Get-CimInstance Win32_Group -Filter ""LocalAccount=True AND SID='S-1-5-32-544'"" -ErrorAction SilentlyContinue).Name
$admins=@()
if($adminGroup){
 try {
  $group=[ADSI](""WinNT://./""+$adminGroup+"",group"")
  $admins=@($group.psbase.Invoke('Members') | ForEach-Object { $_.GetType().InvokeMember('Name','GetProperty',$null,$_,$null) })
 } catch {}
}
if(Get-Command Get-LocalUser -ErrorAction SilentlyContinue){
 Get-LocalUser | Sort-Object Name | ForEach-Object {
  [pscustomobject]@{Name=$_.Name;Enabled=$_.Enabled;LastLogon=if($_.LastLogon){$_.LastLogon.ToString('yyyy-MM-dd HH:mm:ss')}else{''};Description=$_.Description;IsAdministrator=($admins -contains $_.Name)}
 }
}else{
 Get-CimInstance Win32_UserAccount -Filter ""LocalAccount=True"" | Sort-Object Name | ForEach-Object {
  [pscustomobject]@{Name=$_.Name;Enabled=(-not $_.Disabled);LastLogon='';Description=$_.Description;IsAdministrator=($admins -contains $_.Name)}
 }
}
"));

            BindGrid(_adminMembersGrid, await _remote.ExecuteJsonListAsync<LocalGroupMemberInfo>(@"
$adminGroup=(Get-CimInstance Win32_Group -Filter ""LocalAccount=True AND SID='S-1-5-32-544'"" -ErrorAction Stop).Name
$group=[ADSI](""WinNT://./""+$adminGroup+"",group"")
$group.psbase.Invoke('Members') | ForEach-Object {
 $name=$_.GetType().InvokeMember('Name','GetProperty',$null,$_,$null)
 $class=$_.GetType().InvokeMember('Class','GetProperty',$null,$_,$null)
 $path=$_.GetType().InvokeMember('ADsPath','GetProperty',$null,$_,$null)
 [pscustomobject]@{Name=$name;ObjectClass=$class;AdsPath=$path}
}
"));
            WriteAudit("accounts.list", "Локальные пользователи и члены Administrators", true);
        }
        catch (Exception ex) { WriteAudit("accounts.list", ex.Message, false); ShowError(ex); }
    }

    private async Task ChangeLocalAdminMembershipAsync(bool add)
    {
        if (!EnsureConnected()) return;
        var principal = _adminPrincipal.Text.Trim();
        if (string.IsNullOrWhiteSpace(principal)) return;
        var verb = add ? "/add" : "/delete";
        try
        {
            var output = await _remote.ExecuteTextAsync($"$g=(Get-CimInstance Win32_Group -Filter \"LocalAccount=True AND SID='S-1-5-32-544'\").Name; & net.exe localgroup $g '{PsQuote(principal)}' {verb}; if($LASTEXITCODE -ne 0){{throw \"net localgroup exit code $LASTEXITCODE\"}}");
            WriteAudit(add ? "accounts.add-admin" : "accounts.remove-admin", principal, true);
            await RefreshLocalAccountsAsync();
            if (!string.IsNullOrWhiteSpace(output)) _psOutput.AppendText(output);
        }
        catch (Exception ex) { WriteAudit(add ? "accounts.add-admin" : "accounts.remove-admin", $"{principal}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task ToggleSelectedLocalUserAsync(bool enable)
    {
        if (!EnsureConnected() || _accountsGrid.CurrentRow?.DataBoundItem is not LocalAccountInfo user) return;
        try
        {
            var flag = enable ? "no" : "yes";
            await _remote.ExecuteTextAsync($"& net.exe user '{PsQuote(user.Name)}' /active:{flag}; if($LASTEXITCODE -ne 0){{throw \"net user exit code $LASTEXITCODE\"}}");
            WriteAudit(enable ? "accounts.enable" : "accounts.disable", user.Name, true);
            await RefreshLocalAccountsAsync();
        }
        catch (Exception ex) { WriteAudit(enable ? "accounts.enable" : "accounts.disable", $"{user.Name}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshNetworkConfigurationAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var adapters = await _remote.ExecuteJsonListAsync<NetworkAdapterInfo>(@"
Get-NetIPConfiguration -ErrorAction SilentlyContinue | ForEach-Object {
 $a=Get-NetAdapter -InterfaceIndex $_.InterfaceIndex -ErrorAction SilentlyContinue
 [pscustomobject]@{
  InterfaceAlias=$_.InterfaceAlias
  Status=$a.Status
  IPv4Address=(($_.IPv4Address | ForEach-Object IPAddress) -join ', ')
  Gateway=(($_.IPv4DefaultGateway | ForEach-Object NextHop) -join ', ')
  DnsServers=(($_.DNSServer.ServerAddresses) -join ', ')
  MacAddress=$a.MacAddress
  InterfaceIndex=$_.InterfaceIndex
 }
}
");
            BindGrid(_adapterGrid, adapters);
            RememberMacs(CurrentHost, adapters);
            BindGrid(_routeGrid, await _remote.ExecuteJsonListAsync<RouteInfo>(@"
Get-NetRoute -AddressFamily IPv4 -ErrorAction SilentlyContinue | Sort-Object DestinationPrefix,RouteMetric | ForEach-Object {
 [pscustomobject]@{DestinationPrefix=$_.DestinationPrefix;NextHop=$_.NextHop;InterfaceAlias=$_.InterfaceAlias;RouteMetric=$_.RouteMetric}
}
"));
            WriteAudit("network.config", "Получены адаптеры и IPv4 маршруты", true);
        }
        catch (Exception ex) { WriteAudit("network.config", ex.Message, false); ShowError(ex); }
    }

    private async Task RefreshWindowsUpdateAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_hotfixGrid, await _remote.ExecuteJsonListAsync<HotFixInfo>(@"
Get-HotFix -ErrorAction SilentlyContinue | Sort-Object InstalledOn -Descending | Select-Object -First 200 | ForEach-Object {
 [pscustomobject]@{HotFixId=$_.HotFixID;Description=$_.Description;InstalledOn=if($_.InstalledOn){$_.InstalledOn.ToString('yyyy-MM-dd')}else{''};InstalledBy=$_.InstalledBy}
}
"));
            _updateStatus.Text = await _remote.ExecuteTextAsync(@"
'=== Windows Update services ==='
Get-Service wuauserv,bits,cryptsvc,usosvc -ErrorAction SilentlyContinue | Format-Table Name,Status,StartType -AutoSize
''
'=== Pending reboot ==='
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
'PendingReboot: ' + $pending
''
'=== Last boot ==='
(Get-CimInstance Win32_OperatingSystem).LastBootUpTime
");
            WriteAudit("windows-update.diagnostics", "Диагностика и список HotFix", true);
        }
        catch (Exception ex) { WriteAudit("windows-update.diagnostics", ex.Message, false); ShowError(ex); }
    }

    private async Task SearchPendingUpdatesAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            BindGrid(_pendingUpdateGrid, await _remote.ExecuteJsonListAsync<PendingUpdateInfo>(@"
$session=New-Object -ComObject Microsoft.Update.Session
$searcher=$session.CreateUpdateSearcher()
$result=$searcher.Search(""IsInstalled=0 and IsHidden=0"")
foreach($u in $result.Updates){
 [pscustomobject]@{Title=$u.Title;Kb=($u.KBArticleIDs -join ',');IsDownloaded=$u.IsDownloaded;RebootRequired=$u.RebootRequired}
}
"));
            WriteAudit("windows-update.search", $"Найдено: {_pendingUpdateGrid.Rows.Count}", true);
        }
        catch (Exception ex) { WriteAudit("windows-update.search", ex.Message, false); ShowError(ex); }
    }

    private async Task TriggerWindowsUpdateAsync(string action)
    {
        if (!EnsureConnected()) return;
        if (action == "StartInstall" && MessageBox.Show("Запустить установку уже найденных обновлений Windows на удалённом ПК?", "Windows Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var output = await _remote.ExecuteTextAsync($"$p=Get-Command UsoClient.exe -ErrorAction SilentlyContinue; if($p){{ Start-Process UsoClient.exe -ArgumentList '{PsQuote(action)}'; 'Команда UsoClient {PsQuote(action)} отправлена.' }} else {{ throw 'UsoClient.exe не найден на этой версии Windows.' }}");
            _updateStatus.AppendText(Environment.NewLine + output);
            WriteAudit($"windows-update.{action.ToLowerInvariant()}", "Команда отправлена", true);
        }
        catch (Exception ex) { WriteAudit($"windows-update.{action.ToLowerInvariant()}", ex.Message, false); ShowError(ex); }
    }

    private void WriteAudit(string action, string details, bool success, string? host = null)
    {
        _appData.AppendAudit(new AuditEntry
        {
            Time = DateTime.Now,
            Host = host ?? CurrentHost,
            Action = action,
            Details = details,
            Success = success
        });
    }

    private void RefreshAuditGrid()
    {
        BindGrid(_auditGrid, _appData.ReadAudit());
    }

    private void ClearAudit()
    {
        if (MessageBox.Show("Очистить локальный журнал действий?", "Журнал действий", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            if (File.Exists(_appData.AuditPath)) File.Delete(_appData.AuditPath);
            RefreshAuditGrid();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private sealed class RegistryTreeNodeRow
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private sealed class RegistryValueRow
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsKey { get; set; }
    }

    private sealed class FavoriteEditDialog : Form
    {
        private readonly TextBox _name = new() { Dock = DockStyle.Fill };
        private readonly TextBox _host = new() { Dock = DockStyle.Fill };
        private readonly TextBox _group = new() { Dock = DockStyle.Fill };
        private readonly TextBox _notes = new() { Dock = DockStyle.Fill };

        public FavoriteComputer Result { get; private set; }

        public FavoriteEditDialog(FavoriteComputer item)
        {
            Result = item;
            Text = "Избранный компьютер";
            Width = 520;
            Height = 270;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _name.Text = item.Name;
            _host.Text = item.Host;
            _group.Text = item.Group;
            _notes.Text = item.Notes;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 5 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.Controls.Add(new Label { Text = "Имя:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 0);
            grid.Controls.Add(_name, 1, 0);
            grid.Controls.Add(new Label { Text = "DNS / IP:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 1);
            grid.Controls.Add(_host, 1, 1);
            grid.Controls.Add(new Label { Text = "Группа:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 2);
            grid.Controls.Add(_group, 1, 2);
            grid.Controls.Add(new Label { Text = "Примечание:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 3);
            grid.Controls.Add(_notes, 1, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.None, Width = 90 };
            ApplySystemButtonIcon(ok);
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
            ApplySystemButtonIcon(cancel);
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_host.Text))
                {
                    MessageBox.Show("Имя и DNS/IP обязательны.", "Избранное", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Result = new FavoriteComputer
                {
                    Name = _name.Text.Trim(),
                    Host = _host.Text.Trim(),
                    Group = string.IsNullOrWhiteSpace(_group.Text) ? "Общие" : _group.Text.Trim(),
                    Notes = _notes.Text.Trim()
                };
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            grid.Controls.Add(buttons, 1, 4);
            Controls.Add(grid);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
