using DomainAdminConsole.Controls;
using DomainAdminConsole.Models;
using DomainAdminConsole.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DomainAdminConsole;

public sealed partial class MainForm : Form
{
    private readonly DomainService _domain = new();
    private readonly RemotePowerShellService _remote = new();
    private readonly GitHubUpdateService _updates = new();
    private readonly SortableBindingList<DomainComputer> _domainComputers = [];
    private readonly SortableBindingList<DomainComputer> _visibleComputers = [];

    private readonly TextBox _target = new() { Width = 260, PlaceholderText = "IP или DNS имя ПК" };
    private readonly Button _connect = new() { Text = "Подключиться", AutoSize = true };
    private readonly Button _refreshConnected = new() { Text = "Обновить", AutoSize = true, Enabled = false };
    private readonly StatusStrip _statusStrip = new() { SizingGrip = false };
    private readonly ToolStripStatusLabel _connectionStatus = new() { Text = "Не подключено" };
    private readonly ToolStripStatusLabel _domainStatus = new() { Text = "Домен: ..." };
    private readonly ToolStripStatusLabel _scanStatus = new() { Text = "Готово" };
    private readonly ToolStripProgressBar _domainScanProgress = new() { Minimum = 0, Maximum = 1, Value = 0, Width = 190, Visible = false };
    private readonly ToolStripStatusLabel _statusSpring = new() { Spring = true };

    private readonly Panel _busyOverlay = new() { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLightLight, Visible = false };
    private readonly Label _busyOverlayLabel = new() { Text = "Загрузка данных...", AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
    private readonly ProgressBar _busyOverlayProgress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, Width = 280, Height = 18 };

    private readonly TextBox _pcFilter = new() { PlaceholderText = "Фильтр ПК / IP / пользователь", Dock = DockStyle.Fill };
    private readonly CheckBox _onlyActive = new() { Text = "Только активные", Checked = false, AutoSize = true };
    private readonly DataGridView _domainGrid = Grid();
    private readonly Button _refreshDomain = new() { Text = "Обновить домен", AutoSize = true };
    private readonly Button _cancelDomainScan = new() { Text = "Стоп", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _domainScanCts;

    private readonly MenuStrip _mainMenu = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(16, 16) };
    private readonly SingleLineTabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        Appearance = TabAppearance.Normal
    };
    private readonly Button _tabScrollLeft = new()
    {
        Text = "◀", Width = 28, Height = 24, TabStop = false, FlatStyle = FlatStyle.System,
        AccessibleName = "Предыдущая вкладка"
    };
    private readonly Button _tabScrollRight = new()
    {
        Text = "▶", Width = 28, Height = 24, TabStop = false, FlatStyle = FlatStyle.System,
        AccessibleName = "Следующая вкладка"
    };
    private readonly ToolStripMenuItem _favoritesMenu = new("Избранное");
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

    private readonly HashSet<string> _dirtyRemoteTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _tabRefreshGate = new(1, 1);
    private bool _suppressAutoTabRefresh;

    private readonly NotifyIcon _tray = new() { Visible = true, Text = "Domain Admin Console" };
    private bool _reallyExit;

    public MainForm()
    {
        Text = "Domain Admin Console 0.3.7 — BasiliyWolf";
        Width = 1500;
        Height = 900;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        _remote.BusyChanged += RemoteBusyChanged;
        ConfigureTray();
        WireEvents();
        InitializeAdvancedState();
        InitializeV030State();
        InitializeV030ModulesState();

        Shown += async (_, _) =>
        {
            WindowState = FormWindowState.Maximized;
            _tabs.Visible = true;
            _tabs.BringToFront();
            _ = CheckForUpdatesAsync(showNoUpdateMessage: false);
            await LoadDomainAsync();
        };
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
        top.Controls.Add(_refreshConnected);
        ApplySystemButtonIcon(_connect);
        ApplySystemButtonIcon(_refreshConnected);
        ApplySystemButtonIcon(_refreshDomain);
        ApplySystemButtonIcon(_cancelDomainScan);
        ApplySystemButtonIcon(_cancelUserSearch);

        var split = CreateSafeSplitContainer(
            Orientation.Vertical,
            desiredDistance: 390,
            panel1MinSize: 300,
            panel2MinSize: 600);

        split.Panel1.Controls.Add(BuildDomainPanel());
        split.Panel2.Controls.Add(BuildWorkspacePanel());

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

        ConfigureMainMenu();
        ConfigureStatusStrip();

        // Explicit row layout guarantees that the application menu is always
        // the topmost row, followed by the connection toolbar, workspace and status bar.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _mainMenu.Dock = DockStyle.Fill;
        _mainMenu.Margin = new Padding(0);
        top.Dock = DockStyle.Fill;
        top.Margin = new Padding(0);
        split.Dock = DockStyle.Fill;
        split.Margin = new Padding(0);
        _statusStrip.Dock = DockStyle.Fill;
        _statusStrip.Margin = new Padding(0);

        root.Controls.Add(_mainMenu, 0, 0);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(_statusStrip, 0, 3);

        Controls.Clear();
        Controls.Add(root);
        MainMenuStrip = _mainMenu;

        _tabs.Visible = true;
        _tabs.BringToFront();
    }

    private Control BuildWorkspacePanel()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 2, 0, 0) };
        var rightHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 2, 0) };
        _tabScrollLeft.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _tabScrollRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _tabScrollLeft.Location = new Point(1, 2);
        _tabScrollRight.Location = new Point(1, 2);
        _tabScrollLeft.Click += (_, _) => SelectRelativeTab(-1);
        _tabScrollRight.Click += (_, _) => SelectRelativeTab(+1);
        leftHost.Controls.Add(_tabScrollLeft);
        rightHost.Controls.Add(_tabScrollRight);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = SystemColors.ControlLightLight };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var card = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(22), BackColor = SystemColors.Window };
        _busyOverlayLabel.Anchor = AnchorStyles.None;
        _busyOverlayProgress.Anchor = AnchorStyles.None;
        card.Controls.Add(_busyOverlayLabel, 0, 0);
        card.Controls.Add(_busyOverlayProgress, 0, 1);
        center.Controls.Add(card, 1, 1);
        _busyOverlay.Controls.Add(center);

        host.Controls.Add(leftHost, 0, 0);
        host.Controls.Add(_tabs, 1, 0);
        host.Controls.Add(rightHost, 2, 0);
        _tabs.Visible = true;
        _tabs.BringToFront();
        return host;
    }

    private void ConfigureMainMenu()
    {
        _mainMenu.Items.Clear();

        // Top-level menu intentionally uses text only, like standard Windows applications.
        // All action icons inside drop-down menus are generated locally and are DPI-safe.
        var connection = new ToolStripMenuItem("Подключение");
        var domain = new ToolStripMenuItem("Домен");
        var navigation = new ToolStripMenuItem("Разделы");
        var view = new ToolStripMenuItem("Вид");
        var help = new ToolStripMenuItem("Справка");

        connection.DropDownItems.Add(new ToolStripMenuItem("Подключиться", UiIconFactory.Get(UiIconKind.Connect, 16), (_, _) => _connect.PerformClick()));
        connection.DropDownItems.Add(new ToolStripMenuItem("Обновить подключенный ПК", UiIconFactory.Get(UiIconKind.Refresh, 16), (_, _) => _refreshConnected.PerformClick()));
        connection.DropDownItems.Add(new ToolStripSeparator());
        connection.DropDownItems.Add(new ToolStripMenuItem("RDP к текущему ПК", UiIconFactory.Get(UiIconKind.Rdp, 16), (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(CurrentHost)) Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{CurrentHost}") { UseShellExecute = true });
        }));
        connection.DropDownItems.Add(new ToolStripMenuItem("Открыть C$", UiIconFactory.Get(UiIconKind.FolderOpen, 16), (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(CurrentHost)) Process.Start(new ProcessStartInfo($@"\\{CurrentHost}\c$") { UseShellExecute = true });
        }));

        domain.DropDownItems.Add(new ToolStripMenuItem("Обновить список ПК", UiIconFactory.Get(UiIconKind.Refresh, 16), (_, _) => _refreshDomain.PerformClick()));
        domain.DropDownItems.Add(new ToolStripMenuItem("Остановить сканирование", UiIconFactory.Get(UiIconKind.Stop, 16), (_, _) => _cancelDomainScan.PerformClick()));

        _favoritesMenu.DropDownOpening += (_, _) => PopulateMainFavoritesMenu(_favoritesMenu);

        view.DropDownItems.Add(new ToolStripMenuItem("Следующая вкладка", null, (_, _) => SelectRelativeTab(+1)));
        view.DropDownItems.Add(new ToolStripMenuItem("Предыдущая вкладка", null, (_, _) => SelectRelativeTab(-1)));

        help.DropDownItems.Add(new ToolStripMenuItem("Проверить обновления", UiIconFactory.Get(UiIconKind.Refresh, 16),
            async (_, _) => await CheckForUpdatesAsync(showNoUpdateMessage: true)));
        help.DropDownItems.Add(new ToolStripMenuItem("Открыть GitHub", UiIconFactory.Get(UiIconKind.World, 16),
            (_, _) => GitHubUpdateService.OpenRepository()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem("О программе", UiIconFactory.Get(UiIconKind.Info, 16),
            (_, _) => ShowAboutDialog()));

        AddTabNavigationCategory(navigation, "Основное", UiIconKind.Computer,
            "Обзор", "PowerShell", "Файловый менеджер");
        AddTabNavigationCategory(navigation, "Мониторинг", UiIconKind.Info,
            "Процессы", "Службы", "Журналы Windows", "Порты", "Ping / Tracert", "Сеансы");
        AddTabNavigationCategory(navigation, "Домен", UiIconKind.Users,
            "Поиск в домене", "Массовые действия", "Wake-on-LAN", "RDP Shadow");
        AddTabNavigationCategory(navigation, "Администрирование", UiIconKind.Settings,
            "Реестр", "Планировщик", "Программы", "Локальные учётки", "Сеть ПК", "Windows Update",
            "BitLocker / TPM", "Принтеры", "Сертификаты", "Firewall", "SMB Sessions / Files", "Устройства / драйверы");
        AddTabNavigationCategory(navigation, "Инструменты", UiIconKind.Script,
            "PowerShell-скрипты", "Утилиты", "Журнал действий");

        _mainMenu.Items.Add(connection);
        _mainMenu.Items.Add(domain);
        _mainMenu.Items.Add(_favoritesMenu);
        _mainMenu.Items.Add(navigation);
        _mainMenu.Items.Add(view);
        _mainMenu.Items.Add(help);
    }

    private void AddTabNavigationCategory(ToolStripMenuItem parent, string title, UiIconKind icon, params string[] tabNames)
    {
        var category = new ToolStripMenuItem(title, UiIconFactory.Get(icon, 16));
        foreach (var tabName in tabNames)
        {
            var page = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => string.Equals(t.Text, tabName, StringComparison.OrdinalIgnoreCase));
            if (page is null) continue;
            var item = new ToolStripMenuItem(page.Text, GetSystemActionImage(page.Text)) { Tag = page };
            item.Click += (_, _) =>
            {
                _tabs.SelectedTab = page;
                _tabs.Focus();
            };
            category.DropDownItems.Add(item);
        }
        if (category.DropDownItems.Count > 0)
        {
            category.DropDownOpening += (_, _) =>
            {
                foreach (var item in category.DropDownItems.OfType<ToolStripMenuItem>())
                    item.Checked = ReferenceEquals(item.Tag, _tabs.SelectedTab);
            };
            parent.DropDownItems.Add(category);
        }
    }

    private async Task CheckForUpdatesAsync(bool showNoUpdateMessage)
    {
        var result = await _updates.CheckAsync();
        if (IsDisposed) return;

        if (!result.Success)
        {
            if (showNoUpdateMessage)
                MessageBox.Show(this, result.Error ?? "Не удалось проверить обновления.", "Проверка обновлений",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (result.IsUpdateAvailable)
        {
            using var prompt = new UpdateAvailableForm(result);
            prompt.ShowDialog(this);
            switch (prompt.Action)
            {
                case UpdatePromptAction.Install:
                    await InstallUpdateAsync(result);
                    break;
                case UpdatePromptAction.OpenRelease:
                    GitHubUpdateService.OpenRelease(result);
                    break;
            }
        }
        else if (showNoUpdateMessage)
        {
            MessageBox.Show(this, "Установлена актуальная версия Domain Admin Console.",
                "Проверка обновлений", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task InstallUpdateAsync(UpdateCheckResult result)
    {
        if (!result.CanSelfUpdate)
        {
            MessageBox.Show(this,
                "В GitHub Release не найден подходящий файл обновления.\r\n" +
                "Опубликуйте asset DomainAdminConsole-win-x64.zip или откройте страницу релиза.",
                "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            GitHubUpdateService.OpenRelease(result);
            return;
        }

        using var progressForm = new UpdateProgressForm(result.LatestVersion);
        progressForm.Show(this);
        progressForm.Refresh();
        var progress = new Progress<int>(p => progressForm.SetProgress(p));
        var prepared = await _updates.DownloadAndPrepareUpdateAsync(result, progress);
        progressForm.Close();

        if (!prepared.Success)
        {
            MessageBox.Show(this, prepared.Error ?? "Не удалось подготовить обновление.",
                "Ошибка обновления", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!_updates.LaunchPreparedUpdate(prepared))
        {
            MessageBox.Show(this, prepared.Error ?? "Не удалось запустить установщик обновления.",
                "Ошибка обновления", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _reallyExit = true;
        _tray.Visible = false;
        Application.Exit();
    }

    private void ShowAboutDialog()
    {
        using var about = new AboutForm(_updates);
        about.ShowDialog(this);
    }

    private void SelectRelativeTab(int direction)
    {
        if (_tabs.TabPages.Count == 0) return;
        var next = (_tabs.SelectedIndex + direction + _tabs.TabPages.Count) % _tabs.TabPages.Count;
        _tabs.SelectedIndex = next;
        _tabs.Focus();
    }

    private void ConfigureStatusStrip()
    {
        _statusStrip.Items.Clear();
        _statusStrip.Items.Add(new ToolStripStatusLabel("Подключение:"));
        _statusStrip.Items.Add(_connectionStatus);
        _statusStrip.Items.Add(new ToolStripSeparator());
        _statusStrip.Items.Add(_statusSpring);
        _statusStrip.Items.Add(_scanStatus);
        _statusStrip.Items.Add(_domainScanProgress);
        _statusStrip.Items.Add(new ToolStripSeparator());
        _statusStrip.Items.Add(_domainStatus);
    }

    private void RemoteBusyChanged(bool busy)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            if (busy)
            {
                _busyOverlayLabel.Text = string.IsNullOrWhiteSpace(CurrentHost)
                    ? "Подключение..."
                    : $"Загрузка данных с {CurrentHost}...";

                var page = _tabs.SelectedTab;
                if (page is not null)
                {
                    if (!ReferenceEquals(_busyOverlay.Parent, page))
                    {
                        _busyOverlay.Parent?.Controls.Remove(_busyOverlay);
                        page.Controls.Add(_busyOverlay);
                    }
                    _busyOverlay.Dock = DockStyle.Fill;
                    _busyOverlay.Visible = true;
                    _busyOverlay.BringToFront();
                }
            }
            else
            {
                _busyOverlay.Visible = false;
                _tabs.Visible = true;
                _tabs.BringToFront();
            }
        }));
    }

    private Control BuildDomainPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 66, ColumnCount = 1, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        header.Controls.Add(_pcFilter, 0, 0);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(_onlyActive);
        header.Controls.Add(options, 0, 1);

        _domainGrid.AutoGenerateColumns = false;
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DomainComputer.StatusDot), HeaderText = "Статус",
            FillWeight = 28, MinimumWidth = 44, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.Name), HeaderText = "ПК", FillWeight = 95 });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.IpAddress), HeaderText = "IP", FillWeight = 82 });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.ConnectivityText), HeaderText = "Ping / WinRM / SMB", FillWeight = 120 });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DomainComputer.RdpDot), HeaderText = "RDP",
            FillWeight = 28, MinimumWidth = 42, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _domainGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DomainComputer.Users), HeaderText = "Пользователь", FillWeight = 150 });
        _domainGrid.DataSource = _visibleComputers;
        _domainGrid.CellFormatting += DomainGridCellFormatting;

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
        MirrorToolbarToGridContextMenu(_diskGrid, diskPanel);
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
        MirrorToolbarToGridContextMenu(_processGrid, bar);
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
        MirrorToolbarToGridContextMenu(_serviceGrid, bar);
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
        MirrorToolbarToGridContextMenu(_eventGrid, bar);
        tab.Controls.Add(_eventGrid);
        tab.Controls.Add(bar);
        return tab;
    }

    private TabPage BuildPortsTab()
    {
        var tab = new TabPage("Порты");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        bar.Controls.Add(Button("Обновить", async (_, _) => await RefreshPortsAsync()));
        MirrorToolbarToGridContextMenu(_portGrid, bar);
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
        MirrorToolbarToGridContextMenu(_userSearchGrid, userBar);
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
        flow.Controls.Add(Button("GPUpdate /force", async (_, _) => await RunNativeUtilityAsync("gpupdate.exe", "/force", "gpupdate /force"), 170, 44));
        flow.Controls.Add(Button("Flush DNS", async (_, _) => await RunNativeUtilityAsync("ipconfig.exe", "/flushdns", "ipconfig /flushdns"), 170, 44));
        flow.Controls.Add(Button("IPConfig /all", async (_, _) => await RunNativeUtilityAsync("ipconfig.exe", "/all", "ipconfig /all"), 170, 44));
        flow.Controls.Add(Button("Перезагрузить ПК", async (_, _) => await RestartRemoteAsync(), 170, 44));
        flow.Controls.Add(Button("Системная информация", async (_, _) => await ShowSystemInformationAsync(), 170, 44));
        flow.Controls.Add(Button("Обновить политики + DNS", async (_, _) => await RefreshPoliciesAndDnsAsync(), 190, 44));

        var messageBox = new TextBox { Width = 500, PlaceholderText = "Сообщение активному пользователю" };
        flow.Controls.Add(messageBox);
        flow.Controls.Add(Button("Отправить сообщение", async (_, _) => await SendMessageAsync(messageBox.Text), 180, 32));

        tab.Controls.Add(flow);
        return tab;
    }

    private void WireEvents()
    {
        _connect.Click += async (_, _) => await ConnectAsync(_target.Text);
        _refreshConnected.Click += async (_, _) => await RefreshConnectedViewsAsync();
        _tabs.SelectedIndexChanged += async (_, _) => await AutoRefreshSelectedTabAsync();
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
        try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { _tray.Icon = SystemIcons.Application; }
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
            _domainStatus.ForeColor = Color.DodgerBlue;
            _scanStatus.Text = "Получение списка компьютеров из AD...";
            _domainScanProgress.Visible = true;
            _domainScanProgress.Maximum = 1;
            _domainScanProgress.Value = 0;
            var pcs = await _domain.GetDomainComputersAsync(ct);

            _domainComputers.Clear();
            foreach (var pc in pcs) _domainComputers.Add(pc);
            ApplyComputerFilter();
            _scanStatus.Text = $"AD: {pcs.Count} ПК. Проверка online...";
            _domainScanProgress.Maximum = Math.Max(1, pcs.Count);
            _domainScanProgress.Value = 0;

            int completed = 0;
            var progress = new Progress<DomainComputer>(pc =>
            {
                completed++;
                _domainScanProgress.Value = Math.Min(_domainScanProgress.Maximum, completed);
                _scanStatus.Text = $"Проверено {completed}/{pcs.Count}; online: {_domainComputers.Count(x => x.ProbeCompleted && x.IsActive)}; offline: {_domainComputers.Count(x => x.ProbeCompleted && !x.IsActive)}";
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
            _domainScanProgress.Visible = false;
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
                      || pc.Users.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                 .OrderByDescending(pc => pc.IsActive)
                 .ThenBy(pc => pc.Name, StringComparer.CurrentCultureIgnoreCase))
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
        _connectionStatus.ForeColor = Color.DarkOrange;
        try
        {
            var connectionHost = await ResolveConnectionHostAsync(host);
            await _remote.ConnectAsync(connectionHost);
            _connectionStatus.Text = connectionHost.Equals(host, StringComparison.OrdinalIgnoreCase)
                ? $"Подключено: {connectionHost}"
                : $"Подключено: {connectionHost} (введено {host})";
            _connectionStatus.ForeColor = Color.ForestGreen;
            _target.Text = host;
            _psOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Подключено к {connectionHost}\r\n");
            WriteAudit("connection.connect", $"Введено: {host}; WinRM: {connectionHost}", true, connectionHost);
            _refreshConnected.Enabled = true;
            ResetConnectedViews();
            MarkAllRemoteTabsDirty();
            await RefreshSelectedTabAsync(force: true);
        }
        catch (Exception ex)
        {
            _connectionStatus.Text = "Ошибка подключения";
            _connectionStatus.ForeColor = Color.Firebrick;
            _refreshConnected.Enabled = false;
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
            BindGrid(_diskGrid, disks);
            _systemInfo.Text = await GetSystemInformationTextAsync();
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
            string output;
            if (TryParseSimpleNativeCommand(command, out var nativeFile, out var nativeArguments))
                output = await _remote.ExecuteNativeProcessAsync(nativeFile, nativeArguments);
            else
                output = await _remote.ExecuteTextAsync(command);

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
            BindGrid(_processGrid, await _remote.ExecuteJsonListAsync<ProcessInfo>(@"
Get-Process -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | ForEach-Object {
 [pscustomobject]@{ Id=$_.Id; Name=$_.ProcessName; Cpu=[math]::Round([double]$_.CPU,1); MemoryMb=[math]::Round($_.WorkingSet64/1MB,1); UserName='' }
}
"));
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
            BindGrid(_serviceGrid, await _remote.ExecuteJsonListAsync<ServiceInfo>(@"
Get-CimInstance Win32_Service | Sort-Object DisplayName | ForEach-Object {
 [pscustomobject]@{ Name=$_.Name; DisplayName=$_.DisplayName; Status=$_.State; StartType=$_.StartMode }
}
"));
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
            BindGrid(_eventGrid, await _remote.ExecuteJsonListAsync<EventRow>($@"
Get-WinEvent -LogName '{PsQuote(log)}' -MaxEvents {count} -ErrorAction Stop | ForEach-Object {{
 [pscustomobject]@{{ TimeCreated=$_.TimeCreated.ToString('o'); Id=$_.Id; Level=$_.LevelDisplayName; Provider=$_.ProviderName; Message=$_.Message }}
}}
"));
            if (_eventGrid.Columns[nameof(EventRow.Message)] is { } col) col.Width = 650;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RefreshPortsAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            var ports = await _remote.ExecuteJsonListAsync<PortInfo>(@"
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
            foreach (var port in ports)
                port.Description = GetPortDescription(port.LocalPort, port.Protocol, port.Process);
            BindGrid(_portGrid, ports);
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
                output = await _remote.ExecuteNativeProcessAsync(tool == "ping" ? "ping.exe" : "tracert.exe", address);
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
            var normalizedUsers = await _domain.GetLoggedOnUsersTextAsync(CurrentHost);
            var sessions = new List<RdpSessionInfo>();
            try { sessions = await RdpSessionService.GetSessionsAsync(CurrentHost); } catch { }

            var sb = new StringBuilder();
            sb.AppendLine("=== Пользователи ===");
            sb.AppendLine(string.IsNullOrWhiteSpace(normalizedUsers) ? "Нет вошедших пользователей" : normalizedUsers);
            sb.AppendLine();
            sb.AppendLine("=== Сеансы RDP / Console ===");
            if (sessions.Count == 0) sb.AppendLine("Сеансы через WTS API не найдены или недоступны.");
            foreach (var session in sessions)
                sb.AppendLine($"ID {session.Id,-4} {session.State,-14} {session.StationName,-18} {session.UserDisplay}");
            _sessionOutput.Text = sb.ToString();

            var pc = _domainComputers.FirstOrDefault(x => x.Name.Equals(CurrentHost, StringComparison.OrdinalIgnoreCase) || x.DnsHostName.Equals(CurrentHost, StringComparison.OrdinalIgnoreCase));
            if (pc is not null)
            {
                pc.Users = normalizedUsers;
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
            BindGrid(_userSearchGrid, result);
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

    private async Task<string> GetSystemInformationTextAsync()
    {
        return await _remote.ExecuteTextAsync(@"
$os=Get-CimInstance Win32_OperatingSystem
$cs=Get-CimInstance Win32_ComputerSystem
$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
$bios=Get-CimInstance Win32_BIOS | Select-Object -First 1
$ipv4=(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1'} | Select-Object -ExpandProperty IPAddress) -join ', '
$boot=$os.LastBootUpTime
'Имя компьютера : ' + $env:COMPUTERNAME
'Пользователь    : ' + [string]$cs.UserName
'Домен           : ' + [string]$cs.Domain
'ОС              : ' + [string]$os.Caption
'Версия ОС       : ' + [string]$os.Version + ' / Build ' + [string]$os.BuildNumber
'Архитектура     : ' + [string]$os.OSArchitecture
'Производитель   : ' + [string]$cs.Manufacturer
'Модель          : ' + [string]$cs.Model
'Серийный номер  : ' + [string]$bios.SerialNumber
'CPU             : ' + [string]$cpu.Name
'Ядер / потоков  : ' + [string]$cpu.NumberOfCores + ' / ' + [string]$cpu.NumberOfLogicalProcessors
'RAM             : ' + [string]([math]::Round($cs.TotalPhysicalMemory/1GB,2)) + ' ГБ'
'IPv4            : ' + [string]$ipv4
'Последняя загрузка: ' + [string]$boot
'Время работы    : ' + [string](((Get-Date)-$boot).ToString())
");
    }

    private async Task ShowSystemInformationAsync()
    {
        if (!EnsureConnected()) return;
        try
        {
            _systemInfo.Text = await GetSystemInformationTextAsync();
            _suppressAutoTabRefresh = true;
            try { _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Обзор"); }
            finally { _suppressAutoTabRefresh = false; }
            WriteAudit("system.info", "Получена системная информация", true);
        }
        catch (Exception ex) { WriteAudit("system.info", ex.Message, false); ShowError(ex); }
    }

    private async Task RunNativeUtilityAsync(string fileName, string arguments, string displayCommand)
    {
        if (!EnsureConnected()) return;
        try
        {
            var output = await _remote.ExecuteNativeProcessAsync(fileName, arguments);
            _psOutput.AppendText($"\r\nPS {CurrentHost}> {displayCommand}\r\n{output}\r\n");
            _suppressAutoTabRefresh = true;
            try { _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().First(x => x.Text == "PowerShell"); }
            finally { _suppressAutoTabRefresh = false; }
            WriteAudit("utility.execute", displayCommand, true);
        }
        catch (Exception ex) { WriteAudit("utility.execute", $"{displayCommand}: {ex.Message}", false); ShowError(ex); }
    }

    private async Task RefreshPoliciesAndDnsAsync()
    {
        if (!EnsureConnected()) return;
        await RunNativeUtilityAsync("ipconfig.exe", "/flushdns", "ipconfig /flushdns");
        await RunNativeUtilityAsync("gpupdate.exe", "/force", "gpupdate /force");
    }

    private async Task RefreshConnectedViewsAsync()
    {
        if (!EnsureConnected()) return;
        ResetConnectedViews();
        MarkAllRemoteTabsDirty();
        await RefreshSelectedTabAsync(force: true);
    }

    private void MarkAllRemoteTabsDirty()
    {
        _dirtyRemoteTabs.Clear();
        foreach (TabPage tab in _tabs.TabPages)
            _dirtyRemoteTabs.Add(tab.Text);
    }

    private async Task AutoRefreshSelectedTabAsync()
    {
        if (_suppressAutoTabRefresh || !_remote.IsConnected) return;
        await RefreshSelectedTabAsync(force: true);
    }

    private async Task RefreshSelectedTabAsync(bool force)
    {
        if (!_remote.IsConnected || _tabs.SelectedTab is null) return;
        var name = _tabs.SelectedTab.Text;
        if (!force && !_dirtyRemoteTabs.Contains(name)) return;
        if (!await _tabRefreshGate.WaitAsync(0)) return;

        try
        {
            _dirtyRemoteTabs.Remove(name);
            switch (name)
            {
                case "Обзор": await RefreshOverviewAsync(); break;
                case "Файловый менеджер": RefreshLocalFiles(); await RefreshRemoteFilesAsync(); break;
                case "Процессы": await RefreshProcessesAsync(); break;
                case "Службы": await RefreshServicesAsync(); break;
                case "Журналы Windows": await RefreshEventsAsync("System", 100); break;
                case "Порты": await RefreshPortsAsync(); break;
                case "Сеансы": await RefreshSessionsAsync(); break;
                case "RDP Shadow": await RefreshShadowSessionsAsync(); break;
                case "Реестр": await ReadRegistryAsync(); break;
                case "Планировщик": await RefreshScheduledTasksAsync(); break;
                case "Программы": await RefreshInstalledAppsAsync(); break;
                case "Локальные учётки": await RefreshLocalAccountsAsync(); break;
                case "Сеть ПК": await RefreshNetworkConfigurationAsync(); break;
                case "Windows Update": await RefreshWindowsUpdateAsync(); break;
                case "BitLocker / TPM": await RefreshBitLockerTpmAsync(); break;
                case "Принтеры": await RefreshPrintersAsync(); await RefreshPrintJobsAsync(); break;
                case "Сертификаты": await RefreshCertificatesAsync(); break;
                case "Firewall": await RefreshFirewallAsync(); break;
                case "SMB Sessions / Files": await RefreshSmbAsync(); break;
                case "Устройства / драйверы": await RefreshDevicesAsync(); break;
                case "Журнал действий": RefreshAuditGrid(); break;
                case "Массовые действия": LoadBulkTargets(true); break;
            }
        }
        finally { _tabRefreshGate.Release(); }
    }

    private void ResetConnectedViews()
    {
        foreach (var grid in new[]
        {
            _diskGrid, _processGrid, _serviceGrid, _eventGrid, _portGrid, _shadowGrid, _registryGrid,
            _taskGrid, _appsGrid, _accountsGrid, _adminMembersGrid, _adapterGrid, _routeGrid,
            _hotfixGrid, _pendingUpdateGrid, _bitLockerGrid, _printersGrid, _printJobsGrid,
            _certGrid, _firewallProfilesGrid, _firewallRulesGrid, _smbSessionsGrid, _smbOpenFilesGrid,
            _devicesGrid, _remoteFilesGrid
        })
            grid.DataSource = null;

        _systemInfo.Clear();
        _sessionOutput.Clear();
        _networkOutput.Clear();
        _computerUserOutput.Clear();
        _updateStatus.Clear();
        _tpmInfo.Clear();
        _remoteFiles.Clear();
        _installedApps.Clear();
        _bulkResults.Clear();
    }

    private void DomainGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _domainGrid.Rows[e.RowIndex].DataBoundItem is not DomainComputer pc) return;
        var property = _domainGrid.Columns[e.ColumnIndex].DataPropertyName;

        if (property == nameof(DomainComputer.StatusDot))
        {
            // One dot only: green = computer is reachable by at least one probe,
            // red = all probes failed, grey = scan is still running.
            var color = !pc.ProbeCompleted ? Color.DimGray : pc.IsActive ? Color.ForestGreen : Color.Firebrick;
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
            e.CellStyle.Font = new Font(_domainGrid.Font, FontStyle.Bold);
        }
        else if (property == nameof(DomainComputer.ConnectivityText))
        {
            // Ping / WinRM / SMB are shown in one compact comma-separated cell.
            // Reachable services are green; an empty/offline result is grey/red via
            // the row handling below.
            var hasConnectivity = pc.PingOnline || pc.WinRmAvailable || pc.SmbAvailable;
            var color = !pc.ProbeCompleted ? Color.DimGray : hasConnectivity ? Color.ForestGreen : Color.Firebrick;
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
        }
        else if (property == nameof(DomainComputer.RdpDot))
        {
            // RDP is deliberately independent of the general online state.
            var color = !pc.ProbeCompleted ? Color.DimGray : pc.RdpAvailable ? Color.ForestGreen : Color.Firebrick;
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
            e.CellStyle.Font = new Font(_domainGrid.Font, FontStyle.Bold);
        }
        else if (pc.ProbeCompleted && !pc.IsActive)
        {
            e.CellStyle.ForeColor = Color.Firebrick;
            e.CellStyle.SelectionForeColor = Color.Firebrick;
        }
    }

    private static string GetPortDescription(int port, string protocol, string process)
    {
        var description = port switch
        {
            20 => "FTP data", 21 => "FTP", 22 => "SSH / SFTP", 23 => "Telnet", 25 => "SMTP",
            53 => "DNS", 67 => "DHCP Server", 68 => "DHCP Client", 69 => "TFTP", 80 => "HTTP",
            88 => "Kerberos", 110 => "POP3", 123 => "NTP", 135 => "RPC Endpoint Mapper",
            137 => "NetBIOS Name", 138 => "NetBIOS Datagram", 139 => "NetBIOS Session", 143 => "IMAP",
            389 => "LDAP", 443 => "HTTPS", 445 => "SMB / Microsoft-DS", 464 => "Kerberos Password",
            465 => "SMTPS", 514 => "Syslog", 587 => "SMTP Submission", 636 => "LDAPS", 853 => "DNS over TLS",
            993 => "IMAPS", 995 => "POP3S", 1433 => "Microsoft SQL Server", 1434 => "MS SQL Browser",
            1521 => "Oracle Database", 2049 => "NFS", 3306 => "MySQL / MariaDB", 3389 => "RDP",
            5432 => "PostgreSQL", 5900 => "VNC", 5985 => "WinRM HTTP", 5986 => "WinRM HTTPS",
            6379 => "Redis", 8080 => "HTTP alternative", 8443 => "HTTPS alternative", 9200 => "Elasticsearch HTTP",
            9300 => "Elasticsearch transport", 27017 => "MongoDB", _ => ""
        };
        if (!string.IsNullOrWhiteSpace(description)) return description;
        return string.IsNullOrWhiteSpace(process) ? $"{protocol} порт {port}" : $"{process} ({protocol})";
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
        var text = message.Trim();

        try
        {
            try
            {
                var sent = await RdpSessionService.SendMessageAsync(CurrentHost, "Сообщение администратора", text);
                WriteAudit("message.send", $"WTS; длина сообщения: {text.Length}; сеансов: {sent}", true);
                MessageBox.Show($"Сообщение отправлено в активные сеансы: {sent}.", "Сообщение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            catch (Exception wtsError)
            {
                // В некоторых доменах удалённый WTS/RPC закрыт firewall-ом, хотя WinRM работает.
                // Тогда запускаем msg.exe уже внутри подключённого компьютера через WinRM.
                var safe = text.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');
                var output = await _remote.ExecuteNativeProcessAsync("msg.exe", $"* /TIME:60 \"{safe}\"");
                WriteAudit("message.send", $"msg.exe fallback после WTS: {wtsError.Message}; длина={text.Length}", true);
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(output) ? "Команда отправки сообщения выполнена." : output.Trim(),
                    "Сообщение", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            WriteAudit("message.send", ex.Message, false);
            ShowError(ex);
        }
    }

    private static bool TryParseSimpleNativeCommand(string command, out string fileName, out string arguments)
    {
        fileName = string.Empty;
        arguments = string.Empty;
        var trimmed = command.Trim();
        if (trimmed.Length == 0) return false;

        // Если присутствует PowerShell-синтаксис, оставляем обработку самому PowerShell.
        if (trimmed.IndexOfAny(['|', ';', '{', '}', '$', '`', '>', '<']) >= 0) return false;

        var split = trimmed.IndexOfAny([' ', '\t']);
        var token = split < 0 ? trimmed : trimmed[..split];
        arguments = split < 0 ? string.Empty : trimmed[(split + 1)..].Trim();
        var name = Path.GetFileNameWithoutExtension(token).ToLowerInvariant();

        string? exe = name switch
        {
            "systeminfo" => "systeminfo.exe",
            "ipconfig" => "ipconfig.exe",
            "ping" => "ping.exe",
            "tracert" => "tracert.exe",
            "pathping" => "pathping.exe",
            "route" => "route.exe",
            "netstat" => "netstat.exe",
            "arp" => "arp.exe",
            "nslookup" => "nslookup.exe",
            "whoami" => "whoami.exe",
            "hostname" => "hostname.exe",
            "gpresult" => "gpresult.exe",
            "gpupdate" => "gpupdate.exe",
            "driverquery" => "driverquery.exe",
            "tasklist" => "tasklist.exe",
            "taskkill" => "taskkill.exe",
            "quser" => "quser.exe",
            "query" => "query.exe",
            "net" => "net.exe",
            "netsh" => "netsh.exe",
            "sc" => "sc.exe",
            "schtasks" => "schtasks.exe",
            "reg" => "reg.exe",
            "wevtutil" => "wevtutil.exe",
            "wmic" => "wmic.exe",
            "dism" => "dism.exe",
            "sfc" => "sfc.exe",
            "bcdedit" => "bcdedit.exe",
            _ => null
        };

        if (exe is null) return false;
        fileName = exe;
        return true;
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

    private static DataGridView Grid(bool readOnly = true, bool multiSelect = false, bool autoGenerateColumns = true)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = readOnly,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = multiSelect,
            AutoGenerateColumns = autoGenerateColumns,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };

        grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Automatic;
                column.MinimumWidth = Math.Max(column.MinimumWidth, 45);
            }
        };

        grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
            {
                grid.ClearSelection();
                grid.CurrentCell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
                grid.Rows[hit.RowIndex].Selected = true;
            }
        };

        grid.ContextMenuStrip = CreateGridContextMenu(grid);
        return grid;
    }

    private static ContextMenuStrip CreateGridContextMenu(DataGridView grid)
    {
        var menu = new ContextMenuStrip { ImageScalingSize = new Size(16, 16) };
        menu.Items.Add("Копировать ячейку", null, (_, _) => CopyGridCell(grid));
        menu.Items.Add("Копировать строку", null, (_, _) => CopyGridRow(grid));
        menu.Items.Add("Копировать таблицу", null, (_, _) => CopyGridTable(grid));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Растянуть столбцы", null, (_, _) => grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill);
        return menu;
    }

    private static void CopyGridCell(DataGridView grid)
    {
        try { if (grid.CurrentCell?.FormattedValue is not null) Clipboard.SetText(Convert.ToString(grid.CurrentCell.FormattedValue) ?? string.Empty); } catch { }
    }

    private static void CopyGridRow(DataGridView grid)
    {
        if (grid.CurrentRow is null) return;
        try
        {
            var values = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible)
                .Select(c => Convert.ToString(grid.CurrentRow.Cells[c.Index].FormattedValue) ?? string.Empty);
            Clipboard.SetText(string.Join("\t", values));
        }
        catch { }
    }

    private static void CopyGridTable(DataGridView grid)
    {
        try
        {
            var columns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", columns.Select(c => c.HeaderText)));
            foreach (DataGridViewRow row in grid.Rows)
                sb.AppendLine(string.Join("\t", columns.Select(c => Convert.ToString(row.Cells[c.Index].FormattedValue) ?? string.Empty)));
            Clipboard.SetText(sb.ToString());
        }
        catch { }
    }

    private static void BindGrid<T>(DataGridView grid, IEnumerable<T> items)
        => grid.DataSource = new SortableBindingList<T>(items.ToList());

    private static Button Button(string text, EventHandler handler, int width = 100, int height = 28)
    {
        var b = new Button { Text = text, Width = width, Height = height, AutoSize = width == 100 };
        b.Click += handler;
        ApplySystemButtonIcon(b);
        return b;
    }

    private static void ApplySystemButtonIcon(Button button)
    {
        try
        {
            var image = GetSystemActionImage(button.Text);
            if (image is null) return;
            button.Image = image;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        catch { }
    }

    private static Image? GetSystemActionImage(string? actionText)
    {
        var text = (actionText ?? string.Empty).ToLowerInvariant();
        UiIconKind kind;

        if (text.Contains("удал") || text.Contains("закры") || text.Contains("отмен"))
            kind = UiIconKind.Delete;
        else if (text.Contains("останов") || text.Contains("стоп"))
            kind = UiIconKind.Stop;
        else if (text.Contains("выключ") || text.Contains("перез") || text.Contains("restart") || text.Contains("suspend"))
            kind = UiIconKind.Power;
        else if (text.Contains("поиск") || text.Contains("найти") || text.Contains("scan"))
            kind = UiIconKind.Search;
        else if (text.Contains("rdp"))
            kind = UiIconKind.Rdp;
        else if (text.Contains("подключ"))
            kind = UiIconKind.Connect;
        else if (text.Contains("сеть") || text.Contains("ping") || text.Contains("tracert"))
            kind = UiIconKind.Network;
        else if (text.Contains("вверх"))
            kind = UiIconKind.Up;
        else if (text.Contains("назад"))
            kind = UiIconKind.Back;
        else if (text.Contains("папк") || text.Contains("explorer") || text.Contains("откры"))
            kind = UiIconKind.FolderOpen;
        else if (text.Contains("переимен") || text.Contains("измен") || text.Contains("записать"))
            kind = UiIconKind.Edit;
        else if (text.Contains("добав") || text.Contains("создать"))
            kind = UiIconKind.Add;
        else if (text.Contains("принтер") || text.Contains("печать"))
            kind = UiIconKind.Printer;
        else if (text.Contains("сертифик") || text.Contains("ключ"))
            kind = UiIconKind.Key;
        else if (text.Contains("пользоват") || text.Contains("учет") || text.Contains("учёт"))
            kind = UiIconKind.Users;
        else if (text.Contains("систем") || text.Contains("компьют") || text.Contains("пк"))
            kind = UiIconKind.Computer;
        else if (text.Contains("настрой") || text.Contains("политик"))
            kind = UiIconKind.Settings;
        else if (text.Contains("обнов"))
            kind = UiIconKind.Refresh;
        else if (text.Contains("информа") || text.Contains("диагност"))
            kind = UiIconKind.Info;
        else if (text.Contains("скрипт") || text.Contains("powershell"))
            kind = UiIconKind.Script;
        else if (text.Contains("выполн") || text.Contains("запуст") || text.Contains("включ"))
            kind = UiIconKind.Play;
        else
            kind = UiIconKind.Application;

        return UiIconFactory.Get(kind, 16);
    }

    private static void MirrorToolbarToGridContextMenu(DataGridView grid, Control toolbar)
    {
        var buttons = toolbar.Controls.Cast<Control>().OfType<Button>().Where(b => !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (buttons.Count == 0) return;

        var menu = CreateGridContextMenu(grid);
        var mapped = new List<(ToolStripMenuItem Item, Button Button)>();
        var insertIndex = 0;
        foreach (var button in buttons)
        {
            var item = new ToolStripMenuItem(button.Text, GetSystemActionImage(button.Text));
            item.Click += (_, _) => button.PerformClick();
            menu.Items.Insert(insertIndex++, item);
            mapped.Add((item, button));
        }
        menu.Items.Insert(insertIndex, new ToolStripSeparator());
        menu.Opening += (_, _) =>
        {
            foreach (var pair in mapped)
                pair.Item.Enabled = pair.Button.Enabled;
        };
        grid.ContextMenuStrip = menu;
    }

    private static void MirrorAdditionalToolbarButtonsToGridContextMenu(DataGridView grid, Control toolbar)
    {
        var buttons = toolbar.Controls.Cast<Control>().OfType<Button>().Where(b => !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (buttons.Count == 0) return;
        var menu = grid.ContextMenuStrip ?? CreateGridContextMenu(grid);
        var separatorIndex = menu.Items.Cast<ToolStripItem>().Select((item, index) => (item, index))
            .FirstOrDefault(x => x.item is ToolStripSeparator).index;
        if (separatorIndex < 0) separatorIndex = 0;
        var mapped = new List<(ToolStripMenuItem Item, Button Button)>();
        foreach (var button in buttons)
        {
            var item = new ToolStripMenuItem(button.Text, GetSystemActionImage(button.Text));
            item.Click += (_, _) => button.PerformClick();
            menu.Items.Insert(separatorIndex++, item);
            mapped.Add((item, button));
        }
        menu.Opening += (_, _) =>
        {
            foreach (var pair in mapped)
                pair.Item.Enabled = pair.Button.Enabled;
        };
        grid.ContextMenuStrip = menu;
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
                StandardOutputEncoding = GetOemEncoding(),
                StandardErrorEncoding = GetOemEncoding(),
                CreateNoWindow = true
            }
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (await stdout) + (await stderr);
    }

    private static Encoding GetOemEncoding()
    {
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { return Encoding.UTF8; }
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
        WindowState = FormWindowState.Maximized;
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
