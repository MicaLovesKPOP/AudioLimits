using System.ComponentModel;
using System.Runtime.InteropServices;
using AudioLimits.Core.Services;
using Microsoft.UI.Xaml;

namespace AudioLimits.App.Services;

/// <summary>
/// Narrow Win32 system-tray wrapper for the WinUI window. The limiter backend
/// deliberately knows nothing about shell lifecycle.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint TrayIconId = 1;
    private static readonly Guid TrayIconGuid = new("7B38E6E6-3167-44D7-B8C8-6D2AA1B9D2F4");
    private const uint TrayCallbackMessage = 0x8000 + 0x451; // WM_APP + private offset
    private const nuint WindowSubclassId = (nuint)0xA11D10;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetFocus = 0x00000003;
    private const uint NimSetVersion = 0x00000004;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifGuid = 0x00000020;
    private const uint NifShowTip = 0x00000080;
    private const uint NotifyIconVersion4 = 4;

    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinSelect = 0x0400;    // WM_USER + 0
    private const uint NinKeySelect = 0x0401; // WM_USER + 1

    private readonly nint _hWnd;
    private readonly SubclassProc _subclassProc;
    private readonly uint _taskbarCreatedMessage;
    private nint _iconHandle;
    private bool _iconAdded;
    private bool _subclassInstalled;
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler<TrayMenuRequestedEventArgs>? MenuRequested;

    public TrayIconService(Window window)
    {
        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_hWnd == 0)
            throw new InvalidOperationException("Could not get the Audio Limits window handle for the system tray.");

        _subclassProc = WindowSubclassProc;
        if (!SetWindowSubclass(_hWnd, _subclassProc, WindowSubclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the Audio Limits tray message handler.");

        _subclassInstalled = true;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register Explorer restart recovery for the Audio Limits tray icon.");
        }

        try
        {
            _iconHandle = ExtractApplicationIcon();
            AddIcon();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private nint ExtractApplicationIcon()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Could not determine the Audio Limits executable path for the tray icon.");

        var small = new nint[1];
        var count = ExtractIconEx(executable, 0, null, small, 1);
        if (count == 0 || small[0] == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the Audio Limits application icon for the system tray.");

        return small[0];
    }

    private void AddIcon()
    {
        var data = CreateNotifyIconData();
        if (!Shell_NotifyIcon(NimAdd, ref data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not add the Audio Limits system-tray icon.");

        _iconAdded = true;

        // Version 4 gives the modern notification-area callback contract,
        // including WM_CONTEXTMENU for mouse/keyboard context invocation.
        data.uTimeoutOrVersion = NotifyIconVersion4;
        _ = Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private void RecreateIconAfterExplorerRestart()
    {
        if (_disposed)
            return;

        try
        {
            // Explorer has lost its previous notification-area state, even though
            // our local _iconAdded flag is still true.
            var data = CreateNotifyIconData();
            if (!Shell_NotifyIcon(NimAdd, ref data))
            {
                AppLog.Error("Windows could not recreate the Audio Limits tray icon after Explorer restarted.");
                return;
            }

            _iconAdded = true;
            data.uTimeoutOrVersion = NotifyIconVersion4;
            _ = Shell_NotifyIcon(NimSetVersion, ref data);
            AppLog.Info("Recreated Audio Limits tray icon after Explorer restart");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not recreate the Audio Limits tray icon after Explorer restarted", ex);
        }
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hWnd,
        uID = TrayIconId,
        uFlags = NifMessage | NifIcon | NifTip | NifGuid | NifShowTip,
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _iconHandle,
        szTip = "Audio Limits",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
        guidItem = TrayIconGuid
    };

    private nint WindowSubclassProc(
        nint hWnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint refData)
    {
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            RecreateIconAfterExplorerRestart();
            return 0;
        }

        if (message == TrayCallbackMessage)
        {
            var notification = unchecked((uint)((ulong)lParam.ToInt64() & 0xFFFF));
            switch (notification)
            {
                case WmLButtonDblClk:
                case NinSelect:
                case NinKeySelect:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    return 0;

                case WmContextMenu:
                    RequestContextMenu(GetPackedPoint(wParam));
                    return 0;

                case WmRButtonUp: // compatibility fallback if version negotiation is ignored
                    RequestContextMenu(null);
                    return 0;
            }
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private static Point GetPackedPoint(nuint packed) => new()
    {
        X = unchecked((short)(packed & 0xFFFF)),
        Y = unchecked((short)((packed >> 16) & 0xFFFF))
    };

    private void RequestContextMenu(Point? requestedPoint)
    {
        var point = requestedPoint ?? default;
        if ((requestedPoint is null || (point.X == -1 && point.Y == -1)) && !GetCursorPos(out point))
        {
            AppLog.Error("Windows could not determine where to show the Audio Limits tray menu.");
            return;
        }

        MenuRequested?.Invoke(this, new TrayMenuRequestedEventArgs(point.X, point.Y));
    }

    /// <summary>
    /// Returns keyboard focus to the notification-area icon after the WinUI tray
    /// menu is dismissed, matching the Shell_NotifyIcon context-menu contract.
    /// </summary>
    public void RestoreShellFocus()
    {
        if (_disposed || !_iconAdded)
            return;

        try
        {
            var data = CreateNotifyIconData();
            _ = Shell_NotifyIcon(NimSetFocus, ref data);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not restore notification-area focus after dismissing the tray menu", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_iconAdded)
        {
            try
            {
                var data = CreateNotifyIconData();
                _ = Shell_NotifyIcon(NimDelete, ref data);
            }
            catch
            {
                // Process teardown will also remove the notification icon.
            }

            _iconAdded = false;
        }

        if (_subclassInstalled && _hWnd != 0)
        {
            try { _ = RemoveWindowSubclass(_hWnd, _subclassProc, WindowSubclassId); }
            catch { }
            _subclassInstalled = false;
        }

        if (_iconHandle != 0)
        {
            try { _ = DestroyIcon(_iconHandle); }
            catch { }
            _iconHandle = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate nint SubclassProc(
        nint hWnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc callback,
        nuint subclassId,
        nuint refData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc callback,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hWnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ExtractIconEx(
        string file,
        int iconIndex,
        nint[]? largeIcons,
        nint[]? smallIcons,
        uint iconCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

}

public sealed class TrayMenuRequestedEventArgs : EventArgs
{
    public TrayMenuRequestedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}
