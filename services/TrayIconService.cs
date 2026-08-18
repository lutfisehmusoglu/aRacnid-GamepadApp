using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace GamepadApp.Services;

public class TrayIconService : IDisposable
{
    private readonly Window window;
    private readonly HwndSource hwndSource;
    private readonly System.Drawing.Icon icon;
    private readonly nint iconHandle;
    private readonly int taskbarCreatedMessage;

    private bool isVisible;
    private bool iconRegistered;
    private bool disposed;

    private string showMenuText = "Göster";
    private string exitMenuText = "Çıkış";

    private const int WM_TRAYICON = 0x8001;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public TrayIconService(Window window, System.Drawing.Icon icon)
    {
        this.window = window;
        this.icon = icon;

        iconHandle = icon.Handle;
        taskbarCreatedMessage = unchecked((int)RegisterWindowMessage(
            "TaskbarCreated"));

        var helper = new WindowInteropHelper(window);
        nint hwnd = helper.EnsureHandle();

        hwndSource = HwndSource.FromHwnd(hwnd)!;
        hwndSource.AddHook(WndProc);
    }

    public bool Visible
    {
        get => isVisible;
        set
        {
            if (isVisible == value)
            {
                if (value && !iconRegistered && !disposed)
                    AddTrayIcon();
                return;
            }

            isVisible = value;

            if (value)
            {
                AddTrayIcon();
            }
            else
            {
                RemoveTrayIcon();
            }
        }
    }

    public void ShowBalloonTip(
        int timeout,
        string title,
        string text)
    {
        if (!isVisible || !iconRegistered || disposed)
            return;

        var data = new NOTIFYICONDATA();
        data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        data.hWnd = new WindowInteropHelper(window).Handle;
        data.uID = 1;
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = NIIF_INFO;

        data.szInfoTitle = title;
        data.szInfo = text;
        data.uTimeoutOrVersion = (uint)timeout;

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void AddTrayIcon()
    {
        var data = new NOTIFYICONDATA();
        data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        data.hWnd = new WindowInteropHelper(window).Handle;
        data.uID = 1;
        data.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
        data.uCallbackMessage = WM_TRAYICON;
        data.hIcon = iconHandle;
        data.szTip = "aRacnid GamepadApp";

        iconRegistered = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (!iconRegistered)
            return;

        var data = new NOTIFYICONDATA();
        data.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        data.hWnd = new WindowInteropHelper(window).Handle;
        data.uID = 1;

        Shell_NotifyIcon(NIM_DELETE, ref data);
        iconRegistered = false;
    }

    private nint WndProc(
        nint hwnd,
        int msg,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (taskbarCreatedMessage != 0 && msg == taskbarCreatedMessage)
        {
            // Explorer yeniden başladığında bütün notification area ikonlarını
            // siler. Uygulama gizliyse erişim kaybolmasın diye yeniden kaydet.
            iconRegistered = false;
            if (isVisible && !disposed)
                AddTrayIcon();
        }
        else if (msg == WM_TRAYICON)
        {
            int lParamValue = lParam.ToInt32();

            if (lParamValue == WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
                handled = true;
            }
            else if (lParamValue == WM_RBUTTONUP)
            {
                ShowTrayContextMenu();
                handled = true;
            }
        }

        return nint.Zero;
    }

    public void UpdateMenuText(string showText, string exitText)
    {
        showMenuText = showText;
        exitMenuText = exitText;
    }

    private void ShowTrayContextMenu()
    {
        var contextMenu = new ContextMenu();

        var showItem = new MenuItem { Header = showMenuText };
        showItem.Click += (_, _) => ShowRequested?.Invoke();

        var exitItem = new MenuItem { Header = exitMenuText };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        contextMenu.IsOpen = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (isVisible)
        {
            Visible = false;
        }

        hwndSource.RemoveHook(WndProc);
        icon.Dispose();
    }

    // ========================================
    // P/Invoke
    // ========================================

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;

    private const int NIF_ICON = 2;
    private const int NIF_MESSAGE = 1;
    private const int NIF_TIP = 4;
    private const int NIF_INFO = 0x10;

    private const int NIIF_INFO = 1;

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
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
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(
        uint dwMessage,
        ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);
}
