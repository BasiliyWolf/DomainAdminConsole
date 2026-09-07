using DomainAdminConsole.Models;
using System.Text;

namespace DomainAdminConsole;

internal sealed class EventDetailsForm : Form
{
    public EventDetailsForm(EventRow row)
    {
        Text = $"Событие {row.Id} — {row.Provider}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        Size = new Size(920, 650);
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        AddField(header, 0, 0, "Дата и время:", row.TimeCreated?.ToString("dd.MM.yyyy HH:mm:ss") ?? "—");
        AddField(header, 2, 0, "ID события:", row.Id.ToString());
        AddField(header, 0, 1, "Источник:", row.Provider);
        AddField(header, 2, 1, "Уровень:", row.Level);

        var message = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            Font = SystemFonts.MessageBoxFont,
            Text = row.Message ?? string.Empty
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var close = new Button { Text = "Закрыть", Width = 100, Height = 32, DialogResult = DialogResult.OK };
        var copy = new Button { Text = "Копировать", Width = 115, Height = 32 };
        copy.Click += (_, _) =>
        {
            var sb = new StringBuilder();
            var timeText = row.TimeCreated?.ToString("dd.MM.yyyy HH:mm:ss") ?? "—";
            sb.AppendLine("Дата и время: " + timeText);
            sb.AppendLine($"ID события: {row.Id}");
            sb.AppendLine($"Источник: {row.Provider}");
            sb.AppendLine($"Уровень: {row.Level}");
            sb.AppendLine();
            sb.AppendLine(row.Message ?? string.Empty);
            try { Clipboard.SetText(sb.ToString()); } catch { }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(message, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    private static void AddField(TableLayoutPanel table, int column, int row, string caption, string value)
    {
        table.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Margin = new Padding(0, 5, 8, 5)
        }, column, row);
        table.Controls.Add(new Label
        {
            Text = value,
            AutoSize = true,
            Margin = new Padding(0, 5, 18, 5)
        }, column + 1, row);
    }
}
