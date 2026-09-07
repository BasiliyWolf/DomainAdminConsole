using DomainAdminConsole.Services;

namespace DomainAdminConsole;

internal enum UpdatePromptAction
{
    Later,
    OpenRelease,
    Install
}

internal sealed class UpdateAvailableForm : Form
{
    public UpdatePromptAction Action { get; private set; } = UpdatePromptAction.Later;

    public UpdateAvailableForm(UpdateCheckResult result)
    {
        Text = "Доступно обновление";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 220);
        BackColor = SystemColors.Window;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var title = new Label
        {
            Text = $"Доступна новая версия Domain Admin Console {FormatVersion(result.LatestVersion)}",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        };

        var details = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Text = $"Текущая версия: {FormatVersion(result.CurrentVersion)}\r\n" +
                   $"Release: {result.ReleaseName ?? result.LatestTag ?? "GitHub Release"}\r\n" +
                   (result.CanSelfUpdate
                       ? $"Файл обновления: {result.AssetName}"
                       : "В Release нет подходящего ZIP/EXE для автоматического обновления.")
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };

        var later = new Button { Text = "Позже", AutoSize = true, DialogResult = DialogResult.Cancel };
        var open = new Button { Text = "Открыть GitHub", AutoSize = true };
        var install = new Button
        {
            Text = "Обновить сейчас",
            AutoSize = true,
            Enabled = result.CanSelfUpdate,
            Image = UiIconFactory.Get(UiIconKind.Download, 16),
            TextImageRelation = TextImageRelation.ImageBeforeText
        };

        later.Click += (_, _) => { Action = UpdatePromptAction.Later; Close(); };
        open.Click += (_, _) => { Action = UpdatePromptAction.OpenRelease; Close(); };
        install.Click += (_, _) => { Action = UpdatePromptAction.Install; Close(); };

        buttons.Controls.Add(later);
        buttons.Controls.Add(open);
        buttons.Controls.Add(install);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        body.Controls.Add(details);
        details.Location = new Point(20, 70);
        body.Controls.Add(title);
        title.Location = new Point(20, 22);

        Controls.Add(body);
        Controls.Add(buttons);
        CancelButton = later;
        AcceptButton = result.CanSelfUpdate ? install : open;
    }

    private static string FormatVersion(Version? version)
        => version is null ? "?" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
}
