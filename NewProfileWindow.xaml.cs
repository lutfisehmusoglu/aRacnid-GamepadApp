using System.Windows;
using GamepadApp.Services;

namespace GamepadApp;

public partial class NewProfileWindow : Window
{
    public string ProfileName { get; private set; } = "";

    private readonly bool isRenameMode;

    public NewProfileWindow(string? currentName = null)
    {
        isRenameMode = !string.IsNullOrWhiteSpace(currentName);

        InitializeComponent();

        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
        Closed += NewProfileWindow_Closed;

        if (isRenameMode)
        {
            ProfileNameTextBox.Text = currentName;
            ProfileNameTextBox.SelectAll();
        }

        ProfileNameTextBox.Focus();
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(ApplyLocalization);
    }

    private void NewProfileWindow_Closed(
        object? sender,
        EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        Title = isRenameMode
            ? loc.Get("profile.rename")
            : loc.Get("profile.new_window_title");

        NewProfileTitleText.Text = isRenameMode
            ? loc.Get("profile.rename")
            : loc.Get("profile.new_title");

        ProfileNameLabelText.Text = loc.Get("profile.name");
        CancelButton.Content = loc.Get("profile.cancel");
        CreateButton.Content = isRenameMode
            ? loc.Get("profile.rename")
            : loc.Get("profile.create");
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            var loc = LocalizationService.Instance;

            MessageBox.Show(
                loc.Get(isRenameMode
                    ? "profile.rename_empty"
                    : "profile.name_empty"),
                loc.Get("profile.new_window_title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        ProfileName = name;

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
