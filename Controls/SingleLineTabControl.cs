using System.Runtime.InteropServices;

namespace DomainAdminConsole.Controls;

/// <summary>
/// Keeps TabControl headers on one line. The native up/down scroller is hidden
/// because the application exposes dedicated previous/next buttons on the left
/// and right edges of the tab strip.
/// </summary>
internal sealed class SingleLineTabControl : TabControl
{
    private const int SwHide = 0;

    public SingleLineTabControl()
    {
        Multiline = false;
        SizeMode = TabSizeMode.Normal;
        HotTrack = true;
        ShowToolTips = true;
        Padding = new Point(10, 4);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeScrollerDeferred();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        HideNativeScrollerDeferred();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        HideNativeScrollerDeferred();
    }

    private void HideNativeScrollerDeferred()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(new Action(HideNativeScroller));
        }
        catch
        {
            // Control may be disposing during shutdown.
        }
    }

    private void HideNativeScroller()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var child = FindWindowEx(Handle, IntPtr.Zero, "msctls_updown32", null);
        if (child != IntPtr.Zero)
            ShowWindow(child, SwHide);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
