using System.Windows;
using GamepadApp.Services;
using Velopack;

namespace GamepadApp;

public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateInfo? updateInfo;

    public UpdateAvailableWindow(UpdateCheckResult result)
    {
        InitializeComponent();

        updateInfo = result.UpdateInfo;

        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
        Closed += UpdateAvailableWindow_Closed;

        UpdateMessageText.Text =
            LocalizationService.Instance.Get(
                "updates.available_message",
                result.NewVersion ?? "");
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            UpdateMessageText.Text =
                LocalizationService.Instance.Get(
                    "updates.available_message",
                    updateInfo?.TargetFullRelease.Version.ToString() ?? "");
        });
    }

    private void UpdateAvailableWindow_Closed(
        object? sender,
        EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        Title = loc.Get("updates.available_title");
        UpdateTitleText.Text = loc.Get("updates.available_title");
        LaterButton.Content = loc.Get("updates.later");
        UpdateNowButton.Content = loc.Get("updates.update_now");
    }

    private async void UpdateNowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (updateInfo == null)
            return;

        UpdateNowButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        var loc = LocalizationService.Instance;

        var progress = new Progress<int>(value =>
        {
            DownloadProgressBar.Value = value;
            DownloadStatusText.Text =
                loc.Get("updates.downloading", value);
        });

        try
        {
            await UpdateService.Instance
                .DownloadUpdatesAsync(updateInfo, progress);

            DownloadStatusText.Text =
                loc.Get("updates.applying");

            UpdateService.Instance
                .ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateNowButton.IsEnabled = true;
            LaterButton.IsEnabled = true;

            MessageBox.Show(
                loc.Get("updates.download_error", ex.Message),
                loc.Get("updates.available_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LaterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
