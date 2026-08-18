using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamepadApp.Models;
using GamepadApp.Services;
namespace GamepadApp;

public partial class ProfileSelectionWindow : Window
{
    private readonly ProfileService profileService = new ProfileService();
    private readonly SettingsService settingsService = new SettingsService();
    private readonly AppSettings appSettings;

    private List<Profile> profiles = new List<Profile>();
    private string selectedProfileName = "";
    private bool isOpeningProfile;

    public ProfileSelectionWindow()
    {
        InitializeComponent();

        appSettings = settingsService.LoadSettings();
        selectedProfileName = appSettings.LastProfileName;

        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;
        Closing += ProfileSelectionWindow_Closing;
        Closed += ProfileSelectionWindow_Closed;

        LoadProfiles();
    }

    private void ProfileSelectionWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (isOpeningProfile)
            return;

        Profile? selectedProfile = profiles.FirstOrDefault(profile =>
            profile.Name == selectedProfileName);

        if (selectedProfile?.MinimizeToTray == true)
        {
            // Profil ekranına geçerken önceki MainWindow kapatıldığı için
            // tepside çalışmaya devam edecek yeni ana pencereyi gizli oluştur.
            // Aksi halde uyarı çıkmadan uygulamanın tamamı kapanıyordu.
            isOpeningProfile = true;
            _ = new MainWindow(selectedProfile);
            return;
        }

        var loc = LocalizationService.Instance;
        MessageBoxResult result = MessageBox.Show(
            loc.Get("app.exit_confirm_message"),
            loc.Get("app.exit_confirm_title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
            e.Cancel = true;
    }

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(ApplyLocalization);
    }

    private void ProfileSelectionWindow_Closed(
        object? sender,
        EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;
        Closing -= ProfileSelectionWindow_Closing;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        Title = loc.Get("profile.select_window_title");
        SelectTitleText.Text = loc.Get("profile.select_title");
        NewProfileCardText.Text = loc.Get("profile.new_card");
    }
    private void LoadProfiles()
    {
        profiles = profileService.LoadProfiles();
        EnsureSelectedProfile();

        foreach (Profile profile in profiles)
        {
            AddProfileCard(profile);

        }
    }
    private void EditProfileButton_Click(object sender, RoutedEventArgs e)
    {

        if (sender is Button button &&
    button.Tag is Profile profile)
        {
            EditProfileWindow editWindow =
    new EditProfileWindow(profile);

            editWindow.Owner = this;

            bool? result = editWindow.ShowDialog();

            if (result == true &&
                editWindow.RenameRequested &&
                !string.IsNullOrWhiteSpace(editWindow.NewName))
            {
                if (ProfileNameExists(editWindow.NewName, profile))
                {
                    ShowProfileNameExistsMessage();
                    return;
                }

                bool wasSelected = profile.Name == selectedProfileName;
                profile.Name = editWindow.NewName;

                if (wasSelected)
                {
                    selectedProfileName = profile.Name;
                    SaveSelectedProfileName();
                }

                profileService.SaveProfiles(profiles);

                RefreshProfiles();
            }
            if (result == true && editWindow.CopyRequested)
            {
                Profile copiedProfile = CopyProfile(
                    profile,
                    CreateUniqueCopyName(profile.Name));

                profiles.Add(copiedProfile);

                profileService.SaveProfiles(profiles);

                RefreshProfiles();
            }
            if (result == true && editWindow.MakeMainRequested)
            {
                foreach (Profile item in profiles)
                {
                    item.IsMainProfile = false;
                }

                profile.IsMainProfile = true;

                profileService.SaveProfiles(profiles);

                RefreshProfiles();
            }
            if (result == true && editWindow.DeleteRequested)
            {
                if (!profile.IsMainProfile)
                {
                    bool wasSelected = profile.Name == selectedProfileName;
                    profiles.Remove(profile);

                    if (wasSelected)
                        EnsureSelectedProfile();

                    profileService.SaveProfiles(profiles);

                    RefreshProfiles();
                }
            }
        }
    }
    private void RefreshProfiles()
    {
        ProfilesPanel.Children.Clear();

        ProfilesPanel.Children.Add(NewProfileButton);

        foreach (Profile profile in profiles)
        {
            AddProfileCard(profile);
        }
    }

    private void AddProfileCard(Profile profile)
    {
        StackPanel cardContainer = new StackPanel();

        cardContainer.Width = 180;
        cardContainer.Margin = new Thickness(12);


        // PROFİLİN KENDİSİ
        Button profileButton = new Button();

        profileButton.Background = Brushes.Transparent;
        profileButton.BorderThickness = new Thickness(0);
        profileButton.Padding = new Thickness(0);

        profileButton.Tag = profile;
        profileButton.Click += ProfileButton_Click;


        Border profileBorder = new Border();

        profileBorder.Width = 180;
        profileBorder.Height = 210;

        profileBorder.Background =
            new SolidColorBrush(Color.FromRgb(22, 22, 22));

        bool isSelected = profile.Name == selectedProfileName;
        profileBorder.BorderBrush = isSelected
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(42, 42, 42));

        profileBorder.BorderThickness = isSelected
            ? new Thickness(2)
            : new Thickness(1);
        profileBorder.CornerRadius = new CornerRadius(14);
        profileBorder.Padding = new Thickness(14);


        StackPanel profileContent = new StackPanel();


        // GAMEPAD GÖRSEL ALANI
        Border imageArea = new Border();

        imageArea.Width = 140;
        imageArea.Height = 120;

        imageArea.Background =
            new SolidColorBrush(Color.FromRgb(16, 16, 16));

        imageArea.CornerRadius = new CornerRadius(10);
        imageArea.HorizontalAlignment = HorizontalAlignment.Center;


        TextBlock controllerIcon = new TextBlock();

        controllerIcon.Text = "🎮";
        controllerIcon.FontSize = 64;
        controllerIcon.HorizontalAlignment = HorizontalAlignment.Center;
        controllerIcon.VerticalAlignment = VerticalAlignment.Center;

        imageArea.Child = controllerIcon;


        // PROFİL ADI
        TextBlock profileName = new TextBlock();

        profileName.Text = profile.Name;
        profileName.Foreground = Brushes.White;
        profileName.FontSize = 18;
        profileName.FontWeight = FontWeights.SemiBold;
        profileName.HorizontalAlignment = HorizontalAlignment.Center;
        profileName.Margin = new Thickness(0, 14, 0, 0);


        profileContent.Children.Add(imageArea);
        profileContent.Children.Add(profileName);


        // ANA PROFİL YAZISI
        if (profile.IsMainProfile)
        {
            TextBlock mainProfileText = new TextBlock();

            mainProfileText.Text = LocalizationService.Instance.Get(
                "profile.main_badge");

            mainProfileText.Foreground =
                new SolidColorBrush(Color.FromRgb(140, 140, 140));

            mainProfileText.FontSize = 12;
            mainProfileText.HorizontalAlignment = HorizontalAlignment.Center;
            mainProfileText.Margin = new Thickness(0, 4, 0, 0);

            profileContent.Children.Add(mainProfileText);
        }


        profileBorder.Child = profileContent;
        profileButton.Content = profileBorder;


        // DÜZENLE BUTONU
        Button editButton = new Button();

        editButton.Content = "✏";
        editButton.Width = 34;
        editButton.Height = 28;
        editButton.Margin = new Thickness(0, 8, 0, 0);
        editButton.HorizontalAlignment = HorizontalAlignment.Center;

        editButton.Background =
            new SolidColorBrush(Color.FromRgb(28, 28, 28));

        editButton.Foreground = Brushes.White;

        editButton.BorderBrush =
            new SolidColorBrush(Color.FromRgb(50, 50, 50));

        editButton.BorderThickness = new Thickness(1);

        editButton.Tag = profile;
        editButton.Click += EditProfileButton_Click;


        // İKİSİNİ AYNI KART GRUBUNA KOY
        cardContainer.Children.Add(profileButton);
        cardContainer.Children.Add(editButton);


        // YENİ PROFİL BUTONUNDAN ÖNCE EKLE
        int newProfileIndex =
            ProfilesPanel.Children.IndexOf(NewProfileButton);

        if (newProfileIndex >= 0)
        {
            ProfilesPanel.Children.Insert(
                newProfileIndex,
                cardContainer);
        }
        else
        {
            ProfilesPanel.Children.Add(cardContainer);
        }
    }
    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is Profile selectedProfile)
        {
            MainWindow mainWindow = new MainWindow(selectedProfile);

            selectedProfileName = selectedProfile.Name;
            SaveSelectedProfileName();
            isOpeningProfile = true;
            mainWindow.Show();
            Close();
        }
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        NewProfileWindow newProfileWindow = new NewProfileWindow();
        newProfileWindow.Owner = this;

        bool? result = newProfileWindow.ShowDialog();


        if (result == true)
        {
            if (ProfileNameExists(newProfileWindow.ProfileName))
            {
                ShowProfileNameExistsMessage();
                return;
            }

            Profile newProfile = new Profile
            {
                Name = newProfileWindow.ProfileName,
                IsMainProfile = false,
                AdvancedSettingsInitialized = true,
                ControllerSettingsInitialized = true
            };

            profiles.Add(newProfile);

            profileService.SaveProfiles(profiles);

            AddProfileCard(newProfile);
        }
    }

    private void EnsureSelectedProfile()
    {
        Profile? selectedProfile = profiles.FirstOrDefault(profile =>
            profile.Name == selectedProfileName);

        selectedProfile ??= profiles.FirstOrDefault(profile =>
            profile.IsMainProfile);
        selectedProfile ??= profiles.FirstOrDefault();

        selectedProfileName = selectedProfile?.Name ?? "";
        SaveSelectedProfileName();
    }

    private void SaveSelectedProfileName()
    {
        appSettings.LastProfileName = selectedProfileName;
        settingsService.SaveSettings(appSettings);
    }

    private bool ProfileNameExists(
        string profileName,
        Profile? excludedProfile = null)
    {
        return profiles.Any(profile =>
            !ReferenceEquals(profile, excludedProfile) &&
            string.Equals(
                profile.Name,
                profileName,
                StringComparison.OrdinalIgnoreCase));
    }

    private void ShowProfileNameExistsMessage()
    {
        MessageBox.Show(
            LocalizationService.Instance.Get("profile.name_exists"),
            LocalizationService.Instance.Get("profile.select_window_title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private string CreateUniqueCopyName(string sourceName)
    {
        string candidate = $"{sourceName} (Copy)";
        int copyNumber = 2;

        while (ProfileNameExists(candidate))
        {
            candidate = $"{sourceName} (Copy {copyNumber})";
            copyNumber++;
        }

        return candidate;
    }

    private static Profile CopyProfile(
        Profile source,
        string copiedName)
    {
        return new Profile
        {
            Name = copiedName,
            IsMainProfile = false,
            LightbarColors = new List<string>(source.LightbarColors),
            SelectedLightbarColorIndex =
                source.SelectedLightbarColorIndex,
            LightbarEnabled = source.LightbarEnabled,
            RunAtStartup = source.RunAtStartup,
            MinimizeToTray = source.MinimizeToTray,
            ShowConnectionNotifications =
                source.ShowConnectionNotifications,
            Language = source.Language,
            AdvancedSettingsInitialized = true,
            ControllerSettingsInitialized = true,
            ControllerSettings = source.ControllerSettings.Clone()
        };
    }

}
