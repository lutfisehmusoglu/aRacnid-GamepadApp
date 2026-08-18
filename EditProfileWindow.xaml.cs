using System.Windows;
using GamepadApp.Models;
using GamepadApp.Services;

namespace GamepadApp;

public partial class EditProfileWindow : Window
{
    private readonly Profile profile;
    public string? NewName { get; private set; }
    public bool RenameRequested { get; private set; }
    public bool CopyRequested { get; private set; }
    public bool MakeMainRequested { get; private set; }
    public bool DeleteRequested { get; private set; }

    public EditProfileWindow(Profile selectedProfile)
    {
        InitializeComponent();

        profile = selectedProfile;

        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
        Closed += EditProfileWindow_Closed;

        ProfileTitle.Text = profile.Name;

        if (profile.IsMainProfile)
        {
            MakeMainButton.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
        }
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(ApplyLocalization);
    }

    private void EditProfileWindow_Closed(
        object? sender,
        EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        Title = loc.Get("profile.edit_window_title");
        CopyButton.Content = loc.Get("profile.copy");
        DeleteButton.Content = loc.Get("profile.delete");
        MakeMainButton.Content = loc.Get("profile.make_main");

        RenameButton.Content = loc.Get("profile.rename");
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        NewProfileWindow renameWindow =
            new NewProfileWindow(profile.Name);

        renameWindow.Owner = this;

        bool? result = renameWindow.ShowDialog();

        if (result == true)
        {
            NewName = renameWindow.ProfileName;
            RenameRequested = true;

            DialogResult = true;
        }
    }

    // ========================================
    // The Copy, MakeMain, Delete buttons
    // ========================================

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        CopyRequested = true;

        DialogResult = true;
    }

    private void MakeMainButton_Click(object sender, RoutedEventArgs e)
    {
        MakeMainRequested = true;
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        MessageBoxResult result = MessageBox.Show(
            this,
            string.Format(
                loc.Get("profile.delete_confirm"),
                profile.Name),
            loc.Get("profile.delete_confirm_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        DeleteRequested = true;
        DialogResult = true;
    }
}
