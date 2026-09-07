using DomainAdminConsole.Models;
using System.Runtime.InteropServices;

namespace DomainAdminConsole.Services;

public static class RdpSessionService
{
    private const int WTS_CURRENT_SERVER_HANDLE = 0;

    private enum WTS_INFO_CLASS
    {
        WTSInitialProgram,
        WTSApplicationName,
        WTSWorkingDirectory,
        WTSOEMId,
        WTSSessionId,
        WTSUserName,
        WTSWinStationName,
        WTSDomainName
    }

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionID;
        [MarshalAs(UnmanagedType.LPWStr)] public string pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WTSOpenServer(string pServerName);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSCloseServer(IntPtr hServer);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version, out IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS infoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSSendMessage(
        IntPtr hServer,
        int sessionId,
        string pTitle,
        int titleLength,
        string pMessage,
        int messageLength,
        int style,
        int timeout,
        out int pResponse,
        bool wait);

    public static Task<List<RdpSessionInfo>> GetSessionsAsync(string host, CancellationToken cancellationToken = default)
        => Task.Run(() => GetSessions(host, cancellationToken), cancellationToken);

    private static List<RdpSessionInfo> GetSessions(string host, CancellationToken cancellationToken)
    {
        var result = new List<RdpSessionInfo>();
        var server = WTSOpenServer(host);
        if (server == IntPtr.Zero)
            throw new InvalidOperationException($"Не удалось открыть Terminal Services API на {host}. Win32={Marshal.GetLastWin32Error()}");

        try
        {
            if (!WTSEnumerateSessions(server, 0, 1, out var buffer, out var count))
                throw new InvalidOperationException($"WTSEnumerateSessions завершился ошибкой. Win32={Marshal.GetLastWin32Error()}");

            try
            {
                var size = Marshal.SizeOf<WTS_SESSION_INFO>();
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var itemPtr = IntPtr.Add(buffer, i * size);
                    var item = Marshal.PtrToStructure<WTS_SESSION_INFO>(itemPtr);
                    var user = QueryString(server, item.SessionID, WTS_INFO_CLASS.WTSUserName);
                    if (string.IsNullOrWhiteSpace(user)) continue;

                    result.Add(new RdpSessionInfo
                    {
                        Id = item.SessionID,
                        UserName = user,
                        Domain = QueryString(server, item.SessionID, WTS_INFO_CLASS.WTSDomainName),
                        StationName = string.IsNullOrWhiteSpace(item.pWinStationName) ? QueryString(server, item.SessionID, WTS_INFO_CLASS.WTSWinStationName) : item.pWinStationName,
                        State = item.State.ToString().Replace("WTS", "")
                    });
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        finally
        {
            WTSCloseServer(server);
        }

        return result.OrderBy(x => x.Id).ToList();
    }


    public static Task<int> SendMessageAsync(
        string host,
        string title,
        string message,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
        => Task.Run(() => SendMessage(host, title, message, timeoutSeconds, cancellationToken), cancellationToken);

    private static int SendMessage(string host, string title, string message, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var server = WTSOpenServer(host);
        if (server == IntPtr.Zero)
            throw new InvalidOperationException($"Не удалось открыть Terminal Services API на {host}. Win32={Marshal.GetLastWin32Error()}");

        try
        {
            if (!WTSEnumerateSessions(server, 0, 1, out var buffer, out var count))
                throw new InvalidOperationException($"WTSEnumerateSessions завершился ошибкой. Win32={Marshal.GetLastWin32Error()}");

            var sent = 0;
            try
            {
                var size = Marshal.SizeOf<WTS_SESSION_INFO>();
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var itemPtr = IntPtr.Add(buffer, i * size);
                    var item = Marshal.PtrToStructure<WTS_SESSION_INFO>(itemPtr);
                    var user = QueryString(server, item.SessionID, WTS_INFO_CLASS.WTSUserName);
                    if (string.IsNullOrWhiteSpace(user)) continue;
                    if (item.State is not (WTS_CONNECTSTATE_CLASS.WTSActive or WTS_CONNECTSTATE_CLASS.WTSConnected)) continue;

                    const int mbOk = 0x00000000;
                    const int mbIconInformation = 0x00000040;
                    if (WTSSendMessage(
                        server,
                        item.SessionID,
                        title,
                        title.Length * sizeof(char),
                        message,
                        message.Length * sizeof(char),
                        mbOk | mbIconInformation,
                        Math.Clamp(timeoutSeconds, 5, 300),
                        out _,
                        false))
                    {
                        sent++;
                    }
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }

            if (sent == 0)
                throw new InvalidOperationException("Не найдено активных пользовательских сеансов или WTSSendMessage не смог отправить сообщение.");

            return sent;
        }
        finally
        {
            WTSCloseServer(server);
        }
    }

    private static string QueryString(IntPtr server, int sessionId, WTS_INFO_CLASS infoClass)
    {
        if (!WTSQuerySessionInformation(server, sessionId, infoClass, out var buffer, out _))
            return "";

        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }
}
