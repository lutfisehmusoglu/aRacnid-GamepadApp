using System.Runtime.InteropServices;
using System.Windows;
using GamepadApp.Models;
using GamepadApp.Services;

namespace GamepadApp;

public partial class App : Application
{
    private const string MutexName = "GamepadApp_SingleInstance";
    private const string MainWindowTitle = "aRacnid GamepadApp";
    private static readonly string[] AppWindowTitles =
    [
        MainWindowTitle,
        "Profil Seç",
        "Select Profile"
    ];
    private static readonly int WM_RESTORE_APP;

    [DllImport("user32.dll")]
    private static extern nint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint FindWindowEx(
        nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private static readonly int SW_RESTORE = 9;

    static App()
    {
        WM_RESTORE_APP = (int)RegisterWindowMessage("GamepadApp_RestoreWindow");
    }

    private Mutex? mutex;
    private MainWindow? mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            BringExistingInstanceToFront();
            mutex.Dispose();
            Environment.Exit(0);
        }

        base.OnStartup(e);

        var settingsService = new SettingsService();
        var appSettings = settingsService.LoadSettings();

        ProfileService profileService = new ProfileService();

        List<Profile> profiles = profileService.LoadProfiles();

        Profile? selectedProfile =
            profiles.FirstOrDefault(profile =>
                profile.Name == appSettings.LastProfileName);

        selectedProfile ??=
            profiles.FirstOrDefault(profile => profile.IsMainProfile);

        selectedProfile ??= profiles.FirstOrDefault();

        string startupLanguage =
            selectedProfile?.AdvancedSettingsInitialized == true
                ? selectedProfile.Language
                : appSettings.Language;

        LocalizationService.Instance.SetLanguage(startupLanguage);

        if (selectedProfile != null)
        {
            bool startMinimized = e.Args.Contains("--minimized");

            mainWindow = new MainWindow(selectedProfile);

            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }
        else
        {
            ProfileSelectionWindow profileWindow =
                new ProfileSelectionWindow();

            profileWindow.Show();
        }
    }

    public static int RestoreMessageId => WM_RESTORE_APP;

    public void RegisterMainWindow(MainWindow window)
    {
        mainWindow = window;
    }

    private static void BringExistingInstanceToFront()
    {
        nint hwnd = FindWindowByTitle();
        if (hwnd == nint.Zero) return;

        if (IsIconic(hwnd))
            ShowWindowInternal(hwnd, SW_RESTORE);

        PostMessage(hwnd, WM_RESTORE_APP, nint.Zero, nint.Zero);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private static void ShowWindowInternal(nint hWnd, int nCmdShow)
    {
        ShowWindow(hWnd, nCmdShow);
    }

    private static nint FindWindowByTitle()
    {
        foreach (string windowTitle in AppWindowTitles)
        {
            nint directMatch = FindWindow(null, windowTitle);
            if (directMatch != nint.Zero)
                return directMatch;
        }

        nint hwnd = FindWindowEx(nint.Zero, nint.Zero, null, null);

        while (hwnd != nint.Zero)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (AppWindowTitles.Contains(
                    title,
                    StringComparer.Ordinal))
                return hwnd;

            hwnd = FindWindowEx(nint.Zero, hwnd, null, null);
        }

        return nint.Zero;
    }

    public void RestoreMainWindow()
    {
        if (mainWindow == null) return;

        mainWindow.Dispatcher.Invoke(() =>
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
