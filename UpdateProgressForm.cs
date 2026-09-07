namespace DomainAdminConsole;

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _label = new() { AutoSize = true };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Width = 390, Height = 22 };

    public UpdateProgressForm(Version? version)
    {
        Text = "Обновление Domain Admin Console";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 125);

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _label.Text = $"Загрузка версии {FormatVersion(version)}...";
        _label.Location = new Point(24, 24);
        _progress.Location = new Point(24, 58);

        Controls.Add(_label);
        Controls.Add(_progress);
    }

    public void SetProgress(int value)
    {
        if (IsDisposed) return;
        value = Math.Clamp(value, 0, 100);
        _progress.Value = value;
        _label.Text = value < 100 ? $"Загрузка обновления... {value}%" : "Подготовка обновления...";
    }

    private static string FormatVersion(Version? version)
        => version is null ? "?" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
}
