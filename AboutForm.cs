using DomainAdminConsole.Services;
using System.Diagnostics;

namespace DomainAdminConsole;

internal sealed class AboutForm : Form
{
    private readonly GitHubUpdateService _updates;
    private readonly Label _updateStatus = new() { AutoSize = true, MaximumSize = new Size(500, 0) };
    private readonly Button _checkUpdates = new() { Text = "Проверить обновления", AutoSize = true };

    public AboutForm(GitHubUpdateService updates)
    {
        _updates = updates;
        Text = "О программе";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(600, 390);
        BackColor = SystemColors.Window;
        Font = SystemFonts.MessageBoxFont;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            Padding = new Padding(24, 20, 24, 18)
        };
        for (var i = 0; i < root.RowCount; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Domain Admin Console",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 74, 118)
        };

        var version = new Label
        {
            Text = $"Версия: {_updates.CurrentVersion.Major}.{_updates.CurrentVersion.Minor}.{Math.Max(0, _updates.CurrentVersion.Build)}",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold)
        };

        var author = new Label
        {
            Text = "Автор разработки: BasiliyWolf",
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        var description = new Label
        {
            Text = "Инструмент системного администратора для удалённого управления, диагностики и мониторинга компьютеров домена Windows.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Padding = new Padding(0, 12, 0, 12)
        };

        _checkUpdates.Image = UiIconFactory.Get(UiIconKind.Refresh, 16);
        _checkUpdates.ImageAlign = ContentAlignment.MiddleLeft;
        _checkUpdates.TextImageRelation = TextImageRelation.ImageBeforeText;
        _checkUpdates.Padding = new Padding(5, 2, 5, 2);
        _checkUpdates.Click += async (_, _) => await CheckUpdatesAsync();

        var repoText = GitHubUpdateService.RepositoryUri.ToString();
        var github = new LinkLabel
        {
            Text = repoText,
            AutoSize = true,
            LinkArea = new LinkArea(0, repoText.Length),
            Padding = new Padding(0, 4, 0, 0)
        };
        github.LinkClicked += (_, _) => GitHubUpdateService.OpenRepository();

        var copyright = new Label
        {
            Text = "© 2026 BasiliyWolf",
            AutoSize = true,
            Padding = new Padding(0, 14, 0, 0)
        };

        var closePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 14, 0, 0)
        };
        var close = new Button { Text = "Закрыть", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
        close.Click += (_, _) => Close();
        closePanel.Controls.Add(close);

        root.Controls.Add(title);
        root.Controls.Add(version);
        root.Controls.Add(author);
        root.Controls.Add(description);
        root.Controls.Add(_checkUpdates);
        root.Controls.Add(_updateStatus);
        root.Controls.Add(new Label { Text = "GitHub:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
        root.Controls.Add(github);
        root.Controls.Add(copyright);
        root.Controls.Add(new Label { AutoSize = true });
        root.Controls.Add(closePanel);

        Controls.Add(root);
        AcceptButton = close;
    }

    private async Task CheckUpdatesAsync()
    {
        _checkUpdates.Enabled = false;
        _updateStatus.Text = "Проверка GitHub Releases...";
        try
        {
            var result = await _updates.CheckAsync();
            if (!result.Success)
            {
                _updateStatus.Text = $"Не удалось проверить обновления: {result.Error}";
                return;
            }

            if (result.IsUpdateAvailable)
            {
                _updateStatus.Text = $"Доступна новая версия {result.LatestVersion}.";
                using var prompt = new UpdateAvailableForm(result);
                prompt.ShowDialog(this);
                if (prompt.Action == UpdatePromptAction.OpenRelease)
                {
                    GitHubUpdateService.OpenRelease(result);
                }
                else if (prompt.Action == UpdatePromptAction.Install)
                {
                    using var progressForm = new UpdateProgressForm(result.LatestVersion);
                    progressForm.Show(this);
                    progressForm.Refresh();
                    var progress = new Progress<int>(p => progressForm.SetProgress(p));
                    var prepared = await _updates.DownloadAndPrepareUpdateAsync(result, progress);
                    progressForm.Close();
                    if (!prepared.Success || !_updates.LaunchPreparedUpdate(prepared))
                    {
                        MessageBox.Show(this, prepared.Error ?? "Не удалось запустить обновление.",
                            "Ошибка обновления", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        Application.Exit();
                    }
                }
            }
            else
            {
                _updateStatus.Text = "Установлена актуальная версия.";
            }
        }
        finally
        {
            _checkUpdates.Enabled = true;
        }
    }
}
