using System.Runtime.InteropServices;
using AudioLimits.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace AudioLimits.App;

/// <summary>
/// Small WinUI top-level surface used for the notification-area context menu.
/// It deliberately replaces a classic HMENU so the tray menu follows WinUI
/// light/dark/High-Contrast resources and is not z-ordered behind Explorer's
/// notification-area overflow window.
/// </summary>
public enum TrayMenuDismissReason
{
    OutsidePointer,
    Deactivated,
    KeyboardCancel,
    CommandInvoked
}

public sealed class TrayMenuDismissedEventArgs(TrayMenuDismissReason reason) : EventArgs
{
    public TrayMenuDismissReason Reason { get; } = reason;

    // Shell_NotifyIcon(NIM_SETFOCUS) is specifically useful when the user
    // cancels the menu from the keyboard. Do not steal focus back from an
    // explicit pointer click or a command that is activating another window.
    public bool ShouldRestoreShellFocus => Reason == TrayMenuDismissReason.KeyboardCancel;
}

public sealed partial class TrayMenuWindow : Window
{
    private const double MinimumLogicalWidth = 220;
    private const double MaximumLogicalWidth = 360;
    private const int AnchorGapPixels = 2;

    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint VkEscape = 0x1B;
    private const uint VkF4 = 0x73;
    private const uint LlkhfAltDown = 0x20;

    private readonly nint _hWnd;
    private readonly HookProc _mouseHookProc;
    private readonly HookProc _keyboardHookProc;
    private nint _mouseHook;
    private nint _keyboardHook;
    private bool _visible;
    private bool _allowClose;
    private bool _resetFocusOnActivation;

    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<TrayMenuDismissedEventArgs>? MenuDismissed;

    public TrayMenuWindow()
    {
        InitializeComponent();

        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_hWnd == 0)
            throw new InvalidOperationException("Could not get the Audio Limits tray-menu window handle.");

        // Keep the documented context-menu presenter so Windows supplies the
        // appropriate border/corners, but explicitly show it as the foreground
        // window below and add deterministic light-dismiss handling.
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "Audio Limits";

        _mouseHookProc = MouseHookProc;
        _keyboardHookProc = KeyboardHookProc;

        Activated += TrayMenuWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowAt(int screenX, int screenY, bool settingsEnabled)
    {
        // The reusable menu window can retain the last command's FocusState across
        // AppWindow.Hide/Show. Move focus to a real invisible focus sink on every
        // invocation so a command from the previous invocation cannot repaint its
        // keyboard focus rectangle when the menu is shown again.
        SettingsButton.IsEnabled = settingsEnabled;
        _resetFocusOnActivation = true;

        // First place a 1x1 window on the target monitor. GetDpiForWindow then
        // reports that monitor's effective DPI, so the menu keeps the same logical
        // size when invoked from mixed-DPI taskbars/notification areas.
        AppWindow.MoveAndResize(new RectInt32(screenX, screenY, 1, 1));
        var scale = Math.Max(1.0, GetDpiForWindow(_hWnd) / 96.0);

        MenuRoot.InvalidateMeasure();
        MenuRoot.Measure(new Windows.Foundation.Size(MaximumLogicalWidth, double.PositiveInfinity));
        var desired = MenuRoot.DesiredSize;
        var logicalWidth = Math.Clamp(Math.Ceiling(desired.Width), MinimumLogicalWidth, MaximumLogicalWidth);
        var logicalHeight = Math.Max(1, Math.Ceiling(desired.Height));
        var width = Math.Max(1, (int)Math.Ceiling(logicalWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(logicalHeight * scale));

        var point = new PointInt32(screenX, screenY);
        var display = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);
        var work = display?.WorkArea ?? new RectInt32(screenX - width, screenY - height, width, height);

        // Normal notification-area behavior for a bottom taskbar is to grow the
        // menu to the right of the invocation point, so its bottom-left corner is
        // at the icon/cursor. If that would leave the monitor work area, clamp the
        // whole menu back inside the edge instead of letting any part go off-screen.
        var x = ChooseHorizontalPosition(screenX, width, work);
        var y = ChooseVerticalPosition(screenY, height, work);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

        _visible = true;
        InstallDismissHooks();

        // Explicitly show and activate the menu window. SetForegroundWindow is a
        // defensive shell interop step for notification-area/overflow scenarios
        // where Explorer itself may use non-activating surfaces.
        AppWindow.Show(true);
        _ = SetForegroundWindow(_hWnd);

        // Also queue a focus reset as a fallback for shell activation sequences
        // where the Activated event has already fired by the time Show returns.
        DispatcherQueue.TryEnqueue(ResetFocusToSink);
    }

    // External lifecycle callers (for example, restoring the main window from
    // a second launch) only need to dismiss the tray menu without returning
    // focus to Explorer. Keep that public surface parameterless so callers do
    // not need to know about the tray-menu dismissal taxonomy.
    public void HideMenu() => HideMenuCore(TrayMenuDismissReason.Deactivated);

    private void HideMenuCore(TrayMenuDismissReason reason)
    {
        if (!_visible)
            return;

        // Do this while the XAML tree is still live. A command may have keyboard
        // or pointer focus when the window is hidden; without clearing that state,
        // WinUI can restore it on a later Show and draw a stale white focus ring.
        ResetFocusToSink();

        _visible = false;
        _resetFocusOnActivation = false;
        RemoveDismissHooks();
        AppWindow.Hide();
        MenuDismissed?.Invoke(this, new TrayMenuDismissedEventArgs(reason));
    }

    public void CloseForAppExit()
    {
        _allowClose = true;
        _visible = false;
        RemoveDismissHooks();
        Close();
    }

    private static int ChooseHorizontalPosition(int anchorX, int width, RectInt32 work)
    {
        var workRight = work.X + work.Width;

        // Prefer the menu's left edge at the invocation point. Clamping handles
        // right-edge taskbars, tray icons near a screen boundary and narrow work areas.
        return Math.Clamp(anchorX, work.X, Math.Max(work.X, workRight - width));
    }

    private static int ChooseVerticalPosition(int anchorY, int height, RectInt32 work)
    {
        var workBottom = work.Y + work.Height;
        var above = anchorY - height - AnchorGapPixels;
        var below = anchorY + AnchorGapPixels;

        // Prefer above (normal bottom-taskbar placement). If there is not enough
        // room, prefer below; the final clamp guarantees the menu remains visible.
        var y = above >= work.Y ? above : below;
        return Math.Clamp(y, work.Y, Math.Max(work.Y, workBottom - height));
    }

    private void InstallDismissHooks()
    {
        RemoveDismissHooks();

        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, module, 0);
        if (_mouseHook == 0)
            AppLog.Error($"Could not install tray-menu outside-click hook (Win32 {Marshal.GetLastWin32Error()}).");

        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, module, 0);
        if (_keyboardHook == 0)
            AppLog.Error($"Could not install tray-menu keyboard-dismiss hook (Win32 {Marshal.GetLastWin32Error()}).");
    }

    private void RemoveDismissHooks()
    {
        if (_mouseHook != 0)
        {
            _ = UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_keyboardHook != 0)
        {
            _ = UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private nint MouseHookProc(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && _visible && IsPointerButtonDown((uint)wParam))
        {
            var input = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            if (GetWindowRect(_hWnd, out var rect) && !rect.Contains(input.Point.X, input.Point.Y))
            {
                // Observe rather than swallow the outside click. The target window
                // still receives the click while our menu dismisses like a flyout.
                DispatcherQueue.TryEnqueue(() => HideMenuCore(TrayMenuDismissReason.OutsidePointer));
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint KeyboardHookProc(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && _visible && ((uint)wParam is WmKeyDown or WmSysKeyDown))
        {
            var input = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
            var dismiss = input.VirtualKey == VkEscape ||
                          (input.VirtualKey == VkF4 && (input.Flags & LlkhfAltDown) != 0);

            if (dismiss)
            {
                // A native context menu owns Escape/Alt+F4 while open. Consume the
                // key so Alt+F4 cannot leak through and close whatever is underneath.
                DispatcherQueue.TryEnqueue(() => HideMenuCore(TrayMenuDismissReason.KeyboardCancel));
                return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool IsPointerButtonDown(uint message) =>
        message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown;

    private void TrayMenuWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!_visible)
            return;

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideMenuCore(TrayMenuDismissReason.Deactivated);
            return;
        }

        if (_resetFocusOnActivation)
        {
            _resetFocusOnActivation = false;
            DispatcherQueue.TryEnqueue(ResetFocusToSink);
        }
    }

    private void ResetFocusToSink()
    {
        // The sink must be a real tab stop for Control.Focus to accept programmatic
        // focus. It sits outside MenuRoot's command-only Tab cycle, and
        // UseSystemFocusVisuals=False keeps the sink itself invisible.
        _ = FocusSink.Focus(FocusState.Programmatic);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
            return;

        // Alt+F4 while the tray menu has focus should dismiss the menu, not exit
        // the whole application or destroy the reusable menu surface.
        args.Cancel = true;
        HideMenuCore(TrayMenuDismissReason.KeyboardCancel);
    }

    private void MenuRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            HideMenuCore(TrayMenuDismissReason.KeyboardCancel);
            return;
        }

        if (e.Key is not (Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or
                          Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End))
        {
            return;
        }

        var buttons = new[] { OpenButton, SettingsButton, ExitButton }
            .Where(button => button.IsEnabled && button.Visibility == Visibility.Visible)
            .ToArray();
        if (buttons.Length == 0)
            return;

        var current = Array.FindIndex(buttons, button => button.FocusState != FocusState.Unfocused);
        var target = e.Key switch
        {
            Windows.System.VirtualKey.Home => 0,
            Windows.System.VirtualKey.End => buttons.Length - 1,
            Windows.System.VirtualKey.Up => current <= 0 ? buttons.Length - 1 : current - 1,
            _ => current < 0 || current >= buttons.Length - 1 ? 0 : current + 1
        };

        e.Handled = buttons[target].Focus(FocusState.Keyboard);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        HideMenuCore(TrayMenuDismissReason.CommandInvoked);
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideMenuCore(TrayMenuDismissReason.CommandInvoked);
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        HideMenuCore(TrayMenuDismissReason.CommandInvoked);
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
