namespace DomainAdminConsole.Controls;

/// <summary>
/// TabControl used only as a page host. The application draws its own
/// single-line tab strip above the control so the tab headers can be scrolled
/// with dedicated left/right buttons without reserving vertical side gutters.
/// </summary>
internal sealed class SingleLineTabControl : TabControl
{
    private const int TcmAdjustRect = 0x1328;

    public bool HideHeaders { get; set; } = true;

    public SingleLineTabControl()
    {
        Multiline = false;
        HotTrack = false;
        ShowToolTips = false;
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Padding = new Point(0, 0);
    }

    public override Rectangle DisplayRectangle
        => HideHeaders ? ClientRectangle : base.DisplayRectangle;

    protected override void WndProc(ref Message m)
    {
        // Common WinForms technique for a tabless page host. The header row is
        // rendered by MainForm while TabControl keeps all normal page semantics.
        if (HideHeaders && m.Msg == TcmAdjustRect && !DesignMode)
        {
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }
}
