using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamepadApp.Services;

namespace GamepadApp;

public partial class ComponentInstallerWindow : Window
{
    private static readonly Brush InstalledBadgeBrush =
        new SolidColorBrush(Color.FromRgb(26, 58, 26));
    private static readonly Brush MissingBadgeBrush =
        new SolidColorBrush(Color.FromRgb(58, 26, 26));
    private static readonly Brush InstalledTextBrush =
        new SolidColorBrush(Color.FromRgb(76, 175, 80));
    private static readonly Brush MissingTextBrush =
        new SolidColorBrush(Color.FromRgb(244, 67, 54));

    private bool isBusy;

    public ComponentInstallerWindow()
    {
        InitializeComponent();

        ApplyLocalization();
        RefreshComponentStates();

        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            RefreshComponentStates();
        });
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        Title = loc.Get("components.window_title");
        TitleText.Text = loc.Get("components.title");
        SubtitleText.Text = loc.Get("components.subtitle");
        ViGEmRequirementText.Text = loc.Get("components.required");
        HidHideRequirementText.Text = loc.Get("components.recommended");
        ViGEmDescriptionText.Text = loc.Get("components.vigem_desc");
        HidHideDescriptionText.Text = loc.Get("components.hidhide_desc");
        SecurityNoteText.Text = loc.Get("components.security_note");
        CloseButton.Content = loc.Get("components.close");
    }

    private void RefreshComponentStates()
    {
        var loc = LocalizationService.Instance;

        bool viGEmInstalled =
            SystemComponentService.IsViGEmBusInstalled();
        bool hidHideInstalled =
            SystemComponentService.IsHidHideInstalled();

        SetStatus(
            ViGEmStatusBadge,
            ViGEmStatusText,
            viGEmInstalled);
        SetStatus(
            HidHideStatusBadge,
            HidHideStatusText,
            hidHideInstalled);

        ViGEmActionButton.Content = viGEmInstalled
            ? loc.Get("advanced.installed")
            : loc.Get("advanced.install_btn");
        ViGEmActionButton.IsEnabled = !isBusy && !viGEmInstalled;

        HidHideActionButton.Content = hidHideInstalled
            ? loc.Get("advanced.settings_btn")
            : loc.Get("advanced.install_btn");
        HidHideActionButton.IsEnabled = !isBusy;
    }

    private static void SetStatus(
        Border badge,
        TextBlock statusText,
        bool installed)
    {
        var loc = LocalizationService.Instance;

        badge.Background = installed
            ? InstalledBadgeBrush
            : MissingBadgeBrush;
        statusText.Text = installed
            ? loc.Get("advanced.installed")
            : loc.Get("advanced.not_installed");
        statusText.Foreground = installed
            ? InstalledTextBrush
            : MissingTextBrush;
    }

    private async void ViGEmActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await InstallComponentAsync(DependencyComponent.ViGEmBus);
    }

    private async void HidHideActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SystemComponentService.IsHidHideInstalled())
        {
            OpenHidHideConfiguration();
            return;
        }

        await InstallComponentAsync(DependencyComponent.HidHide);
    }

    private async Task InstallComponentAsync(
        DependencyComponent component)
    {
        if (isBusy)
            return;

        var loc = LocalizationService.Instance;
        string componentName = component == DependencyComponent.ViGEmBus
            ? "ViGEmBus"
            : "HidHide";

        MessageBoxResult confirmation = MessageBox.Show(
            string.Format(
                loc.Get("components.install_confirm"),
                componentName),
            loc.Get("components.install_title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (confirmation != MessageBoxResult.OK)
            return;

        SetBusyState(true);

        var progress = new Progress<DependencyInstallProgress>(
            UpdateProgress);

        try
        {
            DependencyInstallResult result =
                await DependencyInstallerService.DownloadAndRunAsync(
                    component,
                    progress);

            RefreshComponentStates();

            if (DependencyInstallerService.IsSuccessfulExitCode(
                    result.ExitCode))
            {
                bool componentDetected =
                    await WaitForComponentDetectionAsync(component);

                if (!componentDetected && !result.RestartRequired)
                {
                    MessageBox.Show(
                        loc.Get("components.install_not_detected"),
                        loc.Get("components.install_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string successMessage = component ==
                    DependencyComponent.ViGEmBus
                    ? loc.Get("components.vigem_success")
                    : loc.Get("components.hidhide_success");

                if (result.RestartRequired)
                {
                    successMessage += "\n\n" +
                        loc.Get("components.restart_required");
                }

                MessageBox.Show(
                    successMessage,
                    loc.Get("components.install_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (result.ExitCode == 1602)
            {
                MessageBox.Show(
                    loc.Get("components.install_cancelled"),
                    loc.Get("components.install_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    string.Format(
                        loc.Get("components.install_exit_error"),
                        result.ExitCode),
                    loc.Get("components.install_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
            when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                loc.Get("components.install_cancelled"),
                loc.Get("components.install_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(
                    loc.Get("components.install_error"),
                    ex.Message),
                loc.Get("components.install_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
            RefreshComponentStates();
        }
    }

    private static async Task<bool> WaitForComponentDetectionAsync(
        DependencyComponent component)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            bool installed = component == DependencyComponent.ViGEmBus
                ? SystemComponentService.IsViGEmBusInstalled()
                : SystemComponentService.IsHidHideInstalled();

            if (installed)
                return true;

            await Task.Delay(250);
        }

        return false;
    }

    private void UpdateProgress(DependencyInstallProgress progress)
    {
        var loc = LocalizationService.Instance;

        switch (progress.Stage)
        {
            case DependencyInstallStage.Downloading:
                InstallProgressBar.IsIndeterminate =
                    progress.Percentage == null;
                InstallProgressBar.Value = progress.Percentage ?? 0;
                ProgressText.Text = progress.Percentage is int percentage
                    ? string.Format(
                        loc.Get("components.downloading_percent"),
                        percentage)
                    : loc.Get("components.downloading");
                break;

            case DependencyInstallStage.Verifying:
                InstallProgressBar.IsIndeterminate = true;
                ProgressText.Text = loc.Get("components.verifying");
                break;

            case DependencyInstallStage.StartingInstaller:
                InstallProgressBar.IsIndeterminate = true;
                ProgressText.Text = loc.Get("components.starting_installer");
                break;

            case DependencyInstallStage.WaitingForInstaller:
                InstallProgressBar.IsIndeterminate = true;
                ProgressText.Text = loc.Get("components.waiting_installer");
                break;
        }
    }

    private void SetBusyState(bool busy)
    {
        isBusy = busy;
        ProgressPanel.Visibility = busy
            ? Visibility.Visible
            : Visibility.Collapsed;
        CloseButton.IsEnabled = !busy;

        if (!busy)
        {
            InstallProgressBar.IsIndeterminate = false;
            InstallProgressBar.Value = 0;
        }

        RefreshComponentStates();
    }

    private void OpenHidHideConfiguration()
    {
        if (SystemComponentService.TryOpenHidHideConfiguration())
            return;

        var loc = LocalizationService.Instance;

        MessageBox.Show(
            loc.Get("advanced.hidhide_not_found"),
            loc.Get("advanced.hidhide_title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (isBusy)
            e.Cancel = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
    }
}
