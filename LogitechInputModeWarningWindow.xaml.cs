using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using GamepadApp.Services;

namespace GamepadApp;

public partial class LogitechInputModeWarningWindow : Window
{
    private const string HelpUrl =
        "https://lutfisehmusoglu.github.io/aRacnid-Website/#faq10";

    private string modelName;
    private bool isDirectInputActive;

    public LogitechInputModeWarningWindow(string modelName)
    {
        this.modelName = modelName;
        InitializeComponent();
        ApplyLocalization();

        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
        Closed += LogitechInputModeWarningWindow_Closed;
    }

    public void ShowDirectInputActive()
    {
        isDirectInputActive = true;
        ApplyLocalization();
        Activate();
    }

    public void ShowXInputWarning(string currentModelName)
    {
        modelName = currentModelName;
        isDirectInputActive = false;
        ApplyLocalization();
        Activate();
    }

    public void RefreshLocalization() => ApplyLocalization();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        if (isDirectInputActive)
        {
            Title = loc.Get("logitech.success_title");
            WarningTitleText.Text = loc.Get("logitech.success_title");
            WarningMessageText.Text =
                loc.Get("logitech.success_message", modelName);
            WarningRecommendationText.Visibility = Visibility.Collapsed;
            HelpLinkButton.Visibility = Visibility.Collapsed;
            StatusIconText.Text = "✓";
            StatusIconText.Foreground =
                new SolidColorBrush(Color.FromRgb(91, 210, 126));
            StatusIconBorder.Background =
                new SolidColorBrush(Color.FromRgb(20, 55, 31));
            StatusIconBorder.BorderBrush =
                new SolidColorBrush(Color.FromRgb(55, 137, 79));
            WarningTitleText.Foreground =
                new SolidColorBrush(Color.FromRgb(91, 210, 126));
            CloseButton.Content = loc.Get("logitech.close");
            return;
        }

        Title = loc.Get("logitech.warning_title");
        WarningTitleText.Text = loc.Get("logitech.warning_title");
        WarningMessageText.Text =
            loc.Get("logitech.warning_message", modelName);
        WarningRecommendationText.Text =
            loc.Get("logitech.warning_recommendation");
        HelpLinkText.Text = loc.Get("logitech.help_link");
        WarningRecommendationText.Visibility = Visibility.Visible;
        HelpLinkButton.Visibility = Visibility.Visible;
        StatusIconText.Text = "⚠";
        StatusIconText.Foreground =
            new SolidColorBrush(Color.FromRgb(255, 107, 107));
        StatusIconBorder.Background =
            new SolidColorBrush(Color.FromRgb(58, 23, 23));
        StatusIconBorder.BorderBrush =
            new SolidColorBrush(Color.FromRgb(182, 66, 66));
        WarningTitleText.Foreground =
            new SolidColorBrush(Color.FromRgb(255, 107, 107));
        CloseButton.Content = loc.Get("logitech.dismiss");
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(ApplyLocalization);
    }

    private void LogitechInputModeWarningWindow_Closed(
        object? sender,
        EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
    }

    private void HelpLinkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(HelpUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                LocalizationService.Instance.Get("logitech.link_error"),
                LocalizationService.Instance.Get("logitech.warning_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
