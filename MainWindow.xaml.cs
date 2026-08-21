using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamepadApp.Models;
using GamepadApp.Services;

namespace GamepadApp;

public partial class MainWindow : Window
{
    private readonly Profile activeProfile;
    private readonly ProfileService profileService = new();
    private readonly GamepadService gamepadService = new GamepadService();
    private readonly ButtonRemapService buttonRemapService = new();
    private readonly VirtualGamepadService virtualGamepadService;
    private readonly GamepadEmulationService? emulationService;
    public VirtualGamepadService VirtualGamepadService =>
    virtualGamepadService;

    public GamepadEmulationService? EmulationService =>
        emulationService;

    public Profile ActiveProfile => activeProfile;

    private readonly DispatcherTimer deviceTimer;

    private string? lastPhysicalDeviceId;

    // ============================================
    // RENK / LIGHTBAR ARAYÜZ DURUMU
    // ============================================

    private readonly List<Color> colorSlots =
    [
                Color.FromRgb(255, 0, 0),     // Kırmızı
                Color.FromRgb(0, 0, 255),     // Mavi
                Color.FromRgb(0, 255, 0),     // Yeşil
                Color.FromRgb(255, 255, 0)    // Sarı
    ];

    private const int MaxColorSlots = 10;

    private int selectedColorSlotIndex = 1;
    private int editingColorSlotIndex = -1;

    private bool colorEditMode;
    private bool isUpdatingColorUi;
    private bool isDraggingColorPicker;
    private bool lightbarEnabled = true;

    private Color draftColor = Color.FromRgb(74, 144, 226);

    // ============================================
    // GELİŞMİŞ SEKME DURUMU
    // ============================================

    private readonly SettingsService settingsService = new();
    private readonly AppSettings appSettings;
    private TrayIconService? trayIcon;
    private bool isReallyClosing;
    private bool isSwitchingProfile;
    private bool wasControllerConnected;
    private string lastControllerName = "";

    public VirtualControllerType CurrentVirtualControllerType { get; private set; }
    private PhysicalControllerType _detectedHardwareType;

    public MainWindow(Profile profile)
    {
        InitializeComponent();

        appSettings = settingsService.LoadSettings();

        activeProfile = profile;
        ((App)Application.Current).RegisterMainWindow(this);
        InitializeActiveProfileSettings();
        LocalizationService.Instance.SetLanguage(activeProfile.Language);
        UpdateWindowTitle();

        LocalizationService.Instance.LanguageChanged +=
            LocalizationService_LanguageChanged;

        GamepadService.SelectedVirtualTypeChanged +=
            GamepadService_SelectedVirtualTypeChanged;

        ApplyLocalization();

        LoadLightbarColorsFromProfile();

        BuildColorSlots();
        LoadColorIntoEditor(colorSlots[selectedColorSlotIndex]);
        SetColorEditMode(false);

        virtualGamepadService =
    new VirtualGamepadService();

        emulationService = new GamepadEmulationService(
            gamepadService,
            virtualGamepadService,
            buttonRemapService,
            activeProfile);

        emulationService.Start();

        SelectNavigationButton(GamepadNavButton);

        SourceInitialized += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProcHook);
        };

        deviceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        deviceTimer.Tick += DeviceTimer_Tick;
        deviceTimer.Start();

        Closed += MainWindow_Closed;

        ApplySettingsToUi();
        UpdateLangButtonStyles();
        CreateNotifyIcon();

        CurrentVersionText.Text =
            UpdateService.Instance.CurrentVersionString;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(StartSilentUpdateCheck));

        RefreshController();

        wasControllerConnected =
            emulationService?.CurrentInput.IsConnected == true;
    }

    private void InitializeActiveProfileSettings()
    {
        if (!activeProfile.AdvancedSettingsInitialized)
        {
            activeProfile.RunAtStartup = appSettings.RunAtStartup;
            activeProfile.MinimizeToTray = appSettings.MinimizeToTray;
            activeProfile.ShowConnectionNotifications =
                appSettings.ShowConnectionNotifications;
            activeProfile.Language = appSettings.Language;
            activeProfile.AdvancedSettingsInitialized = true;
        }

        activeProfile.ControllerSettings ??=
            new ControllerProfileSettings();
        activeProfile.ControllerSettings.ButtonMappings ??= new();

        lightbarEnabled = activeProfile.LightbarEnabled;

        GamepadService.DeadzonePercent =
            activeProfile.ControllerSettings.Deadzone;
        GamepadService.AntiDeadzonePercent =
            activeProfile.ControllerSettings.AntiDeadzone;
        GamepadService.SensitivityPercent =
            activeProfile.ControllerSettings.Sensitivity;
        GamepadService.SelectedVirtualType =
            activeProfile.ControllerSettings.OutputGamepadType == "Xbox360"
                ? VirtualControllerType.Xbox360
                : VirtualControllerType.DualShock4;
        CurrentVirtualControllerType = GamepadService.SelectedVirtualType;

        appSettings.LastProfileName = activeProfile.Name;
        appSettings.Language = activeProfile.Language;
        settingsService.SaveSettings(appSettings);
        settingsService.SetRunAtStartup(activeProfile.RunAtStartup);
        SaveActiveProfile();
    }

    private void SaveActiveProfile()
    {
        profileService.SaveProfile(activeProfile);
    }

    private void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (isReallyClosing || isSwitchingProfile)
            return;

        if (activeProfile.MinimizeToTray)
        {
            if (!ControlsPage.TryCloseKeyBindingsWindow())
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            Hide();
            return;
        }

        var loc = LocalizationService.Instance;

        MessageBoxResult result = MessageBox.Show(
            loc.Get("app.exit_confirm_message"),
            loc.Get("app.exit_confirm_title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        if (!ControlsPage.TryCloseKeyBindingsWindow())
            e.Cancel = true;
    }

    private void MainWindow_Closed(
    object? sender,
    EventArgs e)
    {
        deviceTimer.Stop();

        LocalizationService.Instance.LanguageChanged -=
            LocalizationService_LanguageChanged;

        GamepadService.SelectedVirtualTypeChanged -=
            GamepadService_SelectedVirtualTypeChanged;

        if (_detectedHardwareType == PhysicalControllerType.DualShock4)
        {
            try
            {
                emulationService?.TrySetLightbar(0, 0, 255);
            }
            catch { }
        }

        emulationService?.Stop();

        trayIcon?.Dispose();
    }

    // ============================================
    // LOCALIZATION
    // ============================================

    private void LocalizationService_LanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateWindowTitle();
            ApplyLocalization();
        });
    }

    private void GamepadService_SelectedVirtualTypeChanged(
        VirtualControllerType type)
    {
        Dispatcher.Invoke(() =>
        {
            string oldName =
                GetVirtualDeviceDisplayName(CurrentVirtualControllerType);

            CurrentVirtualControllerType = type;
            RefreshControllerImage();

            bool physicalControllerConnected =
                emulationService?.CurrentInput.IsConnected == true;
            if (!physicalControllerConnected)
            {
                return;
            }

            string newName = GetVirtualDeviceDisplayName(type);

            ShowConnectionNotificationWithName(oldName, false);
            ShowReconnectNotificationAfterDelay(newName);
        });
    }

    private void UpdateWindowTitle()
    {
        Title = LocalizationService.Instance.Get("app.title");
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        GamepadNavButton.Content = loc.Get("nav.gamepad");
        ColorNavButton.Content = loc.Get("nav.color");
        ControlsNavButton.Content = loc.Get("nav.controls");
        AdvancedNavButton.Content = loc.Get("nav.advanced");

        GamepadLabelText.Text = loc.Get("nav.gamepad");
        ConnectionLabelText.Text = loc.Get("gamepad.connection");
        BatteryLabelText.Text = loc.Get("gamepad.battery");
        NoControllerText.Text = loc.Get("gamepad.no_controller");
        ControllerStatusText.Text = loc.Get("gamepad.no_controller");

        ApplySettingsToUi();
        ApplyColorTabLocalization();
        ApplyAdvancedTabLocalization();

        if (emulationService?.CurrentInput.IsConnected == true)
            RefreshController();

        if (trayIcon != null)
            trayIcon.UpdateMenuText(
                loc.Get("tray.show"),
                loc.Get("tray.exit"));
    }

    private void ApplyColorTabLocalization()
    {
        var loc = LocalizationService.Instance;

        ColorTitleText.Text = loc.Get("color.title");
        ColorSubtitleText.Text = loc.Get("color.subtitle");
        ColorPickerTitleText.Text = loc.Get("color.picker_title");
        SelectedColorLabel.Text = loc.Get("color.selected");
        ColorSettingsTitleText.Text = loc.Get("color.settings_title");
        RedLabel.Text = loc.Get("color.red");
        GreenLabel.Text = loc.Get("color.green");
        BlueLabel.Text = loc.Get("color.blue");
        HexLabel.Text = "HEX";
        ColorSlotsLabel.Text = loc.Get("color.colors");
        LightbarTitleText.Text = loc.Get("color.lightbar");
        LightbarDescText.Text = loc.Get("color.lightbar_desc");
        ColorEditHintText.Text = loc.Get("color.edit_hint_default");

        LightbarToggleButton.Content = lightbarEnabled
            ? loc.Get("toggle.on")
            : loc.Get("toggle.off");
    }

    private void ApplyAdvancedTabLocalization()
    {
        var loc = LocalizationService.Instance;

        AdvancedTitleText.Text = loc.Get("advanced.title");
        AdvancedSubtitleText.Text = loc.Get("advanced.subtitle");
        GeneralSectionLabel.Text = loc.Get("advanced.general");
        StartupTitleText.Text = loc.Get("advanced.startup");
        StartupDescText.Text = loc.Get("advanced.startup_desc");
        TrayTitleText.Text = loc.Get("advanced.tray");
        TrayDescText.Text = loc.Get("advanced.tray_desc");
        NotificationsTitleText.Text = loc.Get("advanced.notifications");
        NotificationsDescText.Text = loc.Get("advanced.notifications_desc");
        LanguageTitleText.Text = loc.Get("advanced.language");
        LanguageDescText.Text = loc.Get("advanced.language_desc");
        UpdatesSectionLabel.Text = loc.Get("updates.section");
        UpdatesTitleText.Text = loc.Get("updates.current_version");
        CheckUpdatesButton.Content = loc.Get("updates.check");
        ComponentsSectionLabel.Text = loc.Get("advanced.components");
        ManageComponentsButton.Content =
            loc.Get("advanced.manage_components");
        ViGEmDescText.Text = loc.Get("advanced.vigem_desc");
        HidHideDescText.Text = loc.Get("advanced.hidhide_desc");
        ResetSectionLabel.Text = loc.Get("advanced.reset_section");
        ResetTitleText.Text = loc.Get("advanced.reset");
        ResetDescText.Text = loc.Get("advanced.reset_desc");
        ResetSettingsButton.Content = loc.Get("advanced.reset_btn");

        ViGEmStatusText.Text = loc.Get("advanced.checking");
        HidHideStatusText.Text = loc.Get("advanced.checking");
    }

    // ============================================
    // RENK SEÇİCİ / RENK SLOTLARI
    // ============================================

    private void LoadLightbarColorsFromProfile()
    {
        if (activeProfile.LightbarColors == null ||
            activeProfile.LightbarColors.Count == 0)
        {
            return;
        }

        colorSlots.Clear();

        foreach (string hex in activeProfile.LightbarColors)
        {
            try
            {
                object converted =
                    ColorConverter.ConvertFromString(hex);

                if (converted is Color color)
                {
                    colorSlots.Add(
                        Color.FromRgb(
                            color.R,
                            color.G,
                            color.B));
                }
            }
            catch
            {
                // Bozuk bir renk varsa onu atla.
            }
        }

        // Profil dosyası bozuk veya tüm renkler geçersizse
        // varsayılan dört rengi geri koy.
        if (colorSlots.Count == 0)
        {
            colorSlots.Add(Color.FromRgb(255, 0, 0));
            colorSlots.Add(Color.FromRgb(0, 0, 255));
            colorSlots.Add(Color.FromRgb(0, 255, 0));
            colorSlots.Add(Color.FromRgb(255, 255, 0));
        }

        selectedColorSlotIndex =
            Math.Clamp(
                activeProfile.SelectedLightbarColorIndex,
                0,
                colorSlots.Count - 1);
    }

    private void SaveLightbarColorsToProfile()
    {
        activeProfile.LightbarColors =
            colorSlots
                .Select(color =>
                    $"#{color.R:X2}{color.G:X2}{color.B:X2}")
                .ToList();

        activeProfile.SelectedLightbarColorIndex =
            selectedColorSlotIndex;

        SaveActiveProfile();
    }
    private void ColorSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!colorEditMode || isUpdatingColorUi)
            return;

        Color color = Color.FromRgb(
            (byte)RedSlider.Value,
            (byte)GreenSlider.Value,
            (byte)BlueSlider.Value);

        SetDraftColor(color, updateSliders: false);
    }

    private void BuildColorSlots()
    {
        if (ColorSlotsPanel == null)
            return;

        ColorSlotsPanel.Children.Clear();

        for (int i = 0; i < colorSlots.Count; i++)
        {
            int slotIndex = i;

            Border slotBorder = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(colorSlots[i]),
                BorderBrush = i == selectedColorSlotIndex
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = i == selectedColorSlotIndex
                    ? new Thickness(2)
                    : new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                Tag = slotIndex
            };

            slotBorder.MouseLeftButtonDown +=
                ColorSlot_MouseLeftButtonDown;

            slotBorder.MouseRightButtonDown +=
                ColorSlot_MouseRightButtonDown;

            ColorSlotsPanel.Children.Add(slotBorder);
        }

        Border actionBorder = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(
                Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(75, 75, 75)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        TextBlock actionText = new TextBlock
        {
            Text = colorEditMode
                ? "✓"
                : colorSlots.Count < MaxColorSlots
                    ? "+"
                    : "✎",
            Foreground = Brushes.White,
            FontSize = colorEditMode ? 18 : 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        actionBorder.Child = actionText;
        actionBorder.MouseLeftButtonDown +=
            ColorActionButton_MouseLeftButtonDown;

        ColorSlotsPanel.Children.Add(actionBorder);
    }

    private void ColorSlot_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Border border ||
            border.Tag is not int slotIndex)
            return;

        if (slotIndex < 0 || slotIndex >= colorSlots.Count)
            return;

        if (colorEditMode)
        {
            // + ile düzenleme modu açıldıktan sonra bir mevcut
            // renge tıklanırsa o slot düzenlenir.
            editingColorSlotIndex = slotIndex;
            selectedColorSlotIndex = slotIndex;

            LoadColorIntoEditor(colorSlots[slotIndex]);

            ColorEditHintText.Text =
                LocalizationService.Instance.Get("color.edit_hint_editing");
        }
        else
        {
            // Normal modda sadece hazır rengi seç.
            selectedColorSlotIndex = slotIndex;

            SaveLightbarColorsToProfile();

            LoadColorIntoEditor(colorSlots[slotIndex]);
        }

        BuildColorSlots();
    }

    private void ColorSlot_MouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (colorEditMode)
            return;

        if (sender is not Border border ||
            border.Tag is not int slotIndex)
            return;

        if (slotIndex < 0 || slotIndex >= colorSlots.Count)
            return;

        var loc = LocalizationService.Instance;

        var contextMenu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };

        if (colorSlots.Count > 1)
        {
            var deleteItem = new MenuItem
            {
                Header = loc.Get("profile.delete"),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107))
            };

            deleteItem.Click += (_, _) =>
            {
                colorSlots.RemoveAt(slotIndex);

                if (selectedColorSlotIndex >= colorSlots.Count)
                    selectedColorSlotIndex = colorSlots.Count - 1;

                SaveLightbarColorsToProfile();
                LoadColorIntoEditor(colorSlots[selectedColorSlotIndex]);
                BuildColorSlots();
            };

            contextMenu.Items.Add(deleteItem);
            contextMenu.Items.Add(new Separator());
        }

        var resetItem = new MenuItem
        {
            Header = loc.Get("color.reset_defaults"),
            Foreground = Brushes.White
        };

        resetItem.Click += (_, _) =>
        {
            colorSlots.Clear();
            colorSlots.Add(Color.FromRgb(255, 0, 0));
            colorSlots.Add(Color.FromRgb(0, 0, 255));
            colorSlots.Add(Color.FromRgb(0, 255, 0));
            colorSlots.Add(Color.FromRgb(255, 255, 0));

            selectedColorSlotIndex = 1;

            SaveLightbarColorsToProfile();
            LoadColorIntoEditor(colorSlots[selectedColorSlotIndex]);
            BuildColorSlots();
        };

        contextMenu.Items.Add(resetItem);
        contextMenu.IsOpen = true;
    }

    private void ColorActionButton_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!colorEditMode)
        {
            // İlk 10 slot dolduysa yeni slot eklenmez;
            // mevcut seçili slot düzenlenir.
            if (colorSlots.Count >= MaxColorSlots)
            {
                editingColorSlotIndex =
                    Math.Clamp(
                        selectedColorSlotIndex,
                        0,
                        colorSlots.Count - 1);

                draftColor =
                    colorSlots[editingColorSlotIndex];

                LoadColorIntoEditor(draftColor);

                ColorEditHintText.Text =
                    LocalizationService.Instance.Get("color.edit_hint_limit");
            }
            else
            {
                // Yeni renk, mevcut seçili renkten başlar.
                editingColorSlotIndex = -1;

                Color startingColor =
                    selectedColorSlotIndex >= 0 &&
                    selectedColorSlotIndex < colorSlots.Count
                        ? colorSlots[selectedColorSlotIndex]
                        : Color.FromRgb(74, 144, 226);

                LoadColorIntoEditor(startingColor);

                ColorEditHintText.Text =
                    LocalizationService.Instance.Get("color.edit_hint_new");
            }

            SetColorEditMode(true);
            return;
        }

        // Düzenleme modundaki ✓: kaydet.
        if (editingColorSlotIndex >= 0 &&
            editingColorSlotIndex < colorSlots.Count)
        {
            colorSlots[editingColorSlotIndex] =
                draftColor;

            selectedColorSlotIndex =
                editingColorSlotIndex;
        }
        else if (colorSlots.Count < MaxColorSlots)
        {
            colorSlots.Add(draftColor);
            selectedColorSlotIndex =
                colorSlots.Count - 1;
        }

        SaveLightbarColorsToProfile();

        editingColorSlotIndex = -1;

        SetColorEditMode(false);
        LoadColorIntoEditor(
            colorSlots[selectedColorSlotIndex]);

        ColorEditHintText.Text =
            LocalizationService.Instance.Get("color.edit_hint_default");
    }

    private void SetColorEditMode(bool enabled)
    {
        colorEditMode = enabled;

        if (ColorPickerGrid != null)
        {
            ColorPickerGrid.IsHitTestVisible = enabled;
            ColorPickerGrid.Opacity = enabled ? 1.0 : 0.45;
        }

        if (ColorSettingsGrid != null)
        {
            ColorSettingsGrid.IsHitTestVisible = enabled;
            ColorSettingsGrid.Opacity = enabled ? 1.0 : 0.45;
        }

        BuildColorSlots();
    }

    private void ColorPickerGrid_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!colorEditMode)
            return;

        isDraggingColorPicker = true;

        ColorPickerGrid.CaptureMouse();

        UpdateColorFromPickerPosition(
            e.GetPosition(ColorPickerGrid));
    }

    private void ColorPickerGrid_MouseMove(
     object sender,
     MouseEventArgs e)
    {
        if (!colorEditMode ||
            !isDraggingColorPicker ||
            e.LeftButton != MouseButtonState.Pressed)
            return;

        Point position =
            e.GetPosition(ColorPickerGrid);

        // Mouse paletin dışına çıktıysa artık renk değiştirme.
        if (position.X < 0 ||
            position.Y < 0 ||
            position.X > ColorPickerGrid.ActualWidth ||
            position.Y > ColorPickerGrid.ActualHeight)
            return;

        UpdateColorFromPickerPosition(position);
    }

    private void ColorPickerGrid_MouseLeftButtonUp(
    object sender,
    MouseButtonEventArgs e)
    {
        isDraggingColorPicker = false;

        if (ColorPickerGrid.IsMouseCaptured)
            ColorPickerGrid.ReleaseMouseCapture();
    }

    private void ColorPickerGrid_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (!isDraggingColorPicker)
            return;

        isDraggingColorPicker = false;

        if (ColorPickerGrid.IsMouseCaptured)
            ColorPickerGrid.ReleaseMouseCapture();
    }

    private void UpdateColorFromPickerPosition(Point position)
    {
        double width = Math.Max(1, ColorPickerGrid.ActualWidth);
        double height = Math.Max(1, ColorPickerGrid.ActualHeight);

        // Mouse koordinatını gerçek palet sınırları içinde tut.
        double px = Math.Clamp(position.X, 0, width);
        double py = Math.Clamp(position.Y, 0, height);

        // RENK HESABI
        // Bunlar mutlaka paletin gerçek X/Y konumundan hesaplanmalı.
        double x = px / width;
        double y = py / height;

        Color topColor = GetPaletteTopColor(x);

        double brightness = 1.0 - y;

        Color color = Color.FromRgb(
            (byte)Math.Round(topColor.R * brightness),
            (byte)Math.Round(topColor.G * brightness),
            (byte)Math.Round(topColor.B * brightness));

        // MARKER SINIRI
        // Marker'ın merkezi paletin dışına çıkamaz.
        double halfMarkerWidth = ColorPickerMarker.Width / 2;
        double halfMarkerHeight = ColorPickerMarker.Height / 2;

        double markerX = Math.Clamp(
            px,
            halfMarkerWidth,
            Math.Max(halfMarkerWidth, width - halfMarkerWidth));

        double markerY = Math.Clamp(
            py,
            halfMarkerHeight,
            Math.Max(halfMarkerHeight, height - halfMarkerHeight));

        ColorPickerMarker.Margin = new Thickness(
            markerX - halfMarkerWidth,
            markerY - halfMarkerHeight,
            0,
            0);

        SetDraftColor(color, updateSliders: true);
    }

    private static Color GetPaletteTopColor(double x)
    {
        (double Position, Color Color)[] stops =
        [
            (0.00, Color.FromRgb(255, 255, 255)),
            (0.15, Color.FromRgb(255, 0, 0)),
            (0.32, Color.FromRgb(255, 255, 0)),
            (0.49, Color.FromRgb(0, 255, 0)),
            (0.66, Color.FromRgb(0, 255, 255)),
            (0.83, Color.FromRgb(0, 0, 255)),
            (1.00, Color.FromRgb(255, 0, 255))
        ];

        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (x <= stops[i + 1].Position)
            {
                double range =
                    stops[i + 1].Position -
                    stops[i].Position;

                double amount =
                    range <= 0
                        ? 0
                        : (x - stops[i].Position) /
                          range;

                Color a = stops[i].Color;
                Color b = stops[i + 1].Color;

                return Color.FromRgb(
                    LerpByte(a.R, b.R, amount),
                    LerpByte(a.G, b.G, amount),
                    LerpByte(a.B, b.B, amount));
            }
        }

        return stops[^1].Color;
    }

    private static byte LerpByte(
        byte a,
        byte b,
        double amount)
    {
        return (byte)Math.Round(
            a + ((b - a) * amount));
    }

    private void SetDraftColor(
        Color color,
        bool updateSliders)
    {
        draftColor = color;

        isUpdatingColorUi = true;

        if (updateSliders)
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
        }

        RedValueText.Text = color.R.ToString();
        GreenValueText.Text = color.G.ToString();
        BlueValueText.Text = color.B.ToString();

        string hex =
            $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        PreviewHexText.Text = hex;
        HexValueText.Text = hex;

        if (emulationService?.CurrentDescriptor?.SupportsLightbar == true &&
            lightbarEnabled)
        {
            emulationService.TrySetLightbar(color.R, color.G, color.B);
        }

        SelectedColorSwatch.Background =
            new SolidColorBrush(color);

        isUpdatingColorUi = false;
    }

    private void LoadColorIntoEditor(Color color)
    {
        SetDraftColor(
            color,
            updateSliders: true);
    }

    private void HexValueText_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (!colorEditMode ||
            e.Key != Key.Enter)
            return;

        ApplyHexText();

        Keyboard.ClearFocus();
    }

    private void HexValueText_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (!colorEditMode)
            return;

        ApplyHexText();
    }

    private void ApplyHexText()
    {
        string text =
            HexValueText.Text.Trim();

        if (!text.StartsWith("#"))
            text = "#" + text;

        try
        {
            object converted =
                ColorConverter.ConvertFromString(text);

            if (converted is Color color)
            {
                SetDraftColor(
                    Color.FromRgb(
                        color.R,
                        color.G,
                        color.B),
                    updateSliders: true);

                return;
            }
        }
        catch
        {
            // Geçersiz HEX girilirse mevcut renge dön.
        }

        SetDraftColor(
            draftColor,
            updateSliders: true);
    }

    private void LightbarToggleButton_Click(
     object sender,
     RoutedEventArgs e)
    {
        lightbarEnabled =
            !lightbarEnabled;

        LightbarToggleButton.Content =
            lightbarEnabled
                ? LocalizationService.Instance.Get("toggle.on")
                : LocalizationService.Instance.Get("toggle.off");

        LightbarToggleButton.Opacity =
            lightbarEnabled
                ? 1.0
                : 0.65;

        activeProfile.LightbarEnabled = lightbarEnabled;
        SaveActiveProfile();

        if (emulationService?.CurrentDescriptor?.SupportsLightbar != true)
            return;

        if (lightbarEnabled)
        {
            // Son seçili rengi geri yükle.
            emulationService.TrySetLightbar(
                draftColor.R,
                draftColor.G,
                draftColor.B);
        }
        else
        {
            // Siyah sadece fiziksel ışığı kapatmak için kullanılır.
            // Renk slotlarına veya arayüze eklenmez.
            emulationService.TrySetLightbar(0, 0, 0);
        }
    }

    // ============================================
    // GAMEPAD DURUMU
    // ============================================

    private void DeviceTimer_Tick(
        object? sender,
        EventArgs e)
    {
        RefreshController();

        if (MainTabs.SelectedIndex == 3)
        {
            CheckSystemComponents();
        }
    }

    private void RefreshController()
    {
        try
        {
            PhysicalGamepadDescriptor? descriptor =
                emulationService?.CurrentDescriptor;

            if (descriptor == null ||
                emulationService?.CurrentInput.IsConnected != true)
            {
                SetDisconnectedState();
                return;
            }

            _detectedHardwareType = descriptor.ControllerType;

            bool isNewPhysicalDevice =
                !string.Equals(
                    lastPhysicalDeviceId,
                    descriptor.DeviceId,
                    StringComparison.OrdinalIgnoreCase);

            if (isNewPhysicalDevice && descriptor.SupportsLightbar)
            {
                try
                {
                    Color selectedColor = lightbarEnabled &&
                                          colorSlots.Count > 0 &&
                                          selectedColorSlotIndex >= 0 &&
                                          selectedColorSlotIndex < colorSlots.Count
                        ? colorSlots[selectedColorSlotIndex]
                        : Color.FromRgb(0, 0, 0);

                    emulationService.TrySetLightbar(
                        selectedColor.R,
                        selectedColor.G,
                        selectedColor.B);
                }
                catch { }
            }

            lastPhysicalDeviceId = descriptor.DeviceId;

            string displayName = descriptor.DisplayName;

            lastControllerName = displayName;

            ControllerStatusText.Text = displayName;
            ConnectionText.Text = descriptor.ConnectionDisplayName;

            int? battery =
                emulationService.CurrentInput.BatteryPercentage;

            BatteryText.Text = battery.HasValue
                ? $"{battery.Value} %"
                : LocalizationService.Instance.Get(
                    "gamepad.battery_unavailable");

            DeviceInfoPanel.Opacity = 1;
            DeviceInfoPanel.IsEnabled = true;

            RefreshControllerImage();

            ControllerImage.Visibility =
                Visibility.Visible;

            NoControllerPanel.Visibility =
                Visibility.Collapsed;

            if (!wasControllerConnected)
            {
                ShowConnectionNotification(true);
                wasControllerConnected = true;
            }
        }
        catch
        {
            SetDisconnectedState();
        }
    }

    private void SetDisconnectedState()
    {
        lastPhysicalDeviceId = null;
        _detectedHardwareType = PhysicalControllerType.Unknown;


        ControllerStatusText.Text =
            LocalizationService.Instance.Get("gamepad.no_controller");

        ConnectionText.Text =
            "—";

        BatteryText.Text =
            "—";

        DeviceInfoPanel.Opacity =
            0.45;

        DeviceInfoPanel.IsEnabled =
            false;

        ControllerImage.Visibility =
            Visibility.Collapsed;

        NoControllerPanel.Visibility =
            Visibility.Visible;

        if (wasControllerConnected)
        {
            ShowConnectionNotification(false);
            wasControllerConnected = false;
        }
    }

    // ============================================
    // BAĞLI KOLA GÖRE GÖRSEL
    // ============================================

    private void RefreshControllerImage()
    {
        string imagePath = GamepadService.SelectedVirtualType switch
        {
            VirtualControllerType.Xbox360 => "/assets/controller2.png",
            _ => "/assets/controller.png"
        };

        ControllerImage.Source =
            new BitmapImage(
                new Uri(
                    $"pack://application:,,,{imagePath}",
                    UriKind.Absolute));
    }

    // ============================================
    // PROFİL SEÇİMİNE DÖN
    // ============================================

    private void ProfileButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ControlsPage.TryCloseKeyBindingsWindow())
            return;

        ProfileSelectionWindow profileWindow =
            new ProfileSelectionWindow();

        profileWindow.Show();

        isSwitchingProfile = true;
        Close();
    }

    // ============================================
    // SEKME NAVİGASYONU
    // ============================================

    private void GamepadTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 0;

        SelectNavigationButton(
            GamepadNavButton);
    }

    private void ColorTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 1;

        SelectNavigationButton(
            ColorNavButton);
    }

    private void ControlsTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 2;

        SelectNavigationButton(
            ControlsNavButton);
    }

    private void AdvancedTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 3;

        SelectNavigationButton(
            AdvancedNavButton);

        CheckSystemComponents();
    }

    // ============================================
    // GELİŞMİŞ SEKME
    // ============================================

    private void CheckSystemComponents()
    {
        CheckViGEmBus();
        CheckHidHide();
    }

    private void ShowConnectionNotification(bool connected)
    {
        if (!activeProfile.ShowConnectionNotifications)
            return;

        if (trayIcon == null)
            return;

        string deviceName = string.IsNullOrEmpty(lastControllerName)
            ? "Gamepad"
            : lastControllerName;

        ShowConnectionNotificationWithName(deviceName, connected);
    }

    private void ShowConnectionNotificationWithName(
        string deviceName,
        bool connected)
    {
        if (!activeProfile.ShowConnectionNotifications)
            return;

        if (trayIcon == null)
            return;

        var loc = LocalizationService.Instance;

        string message = connected
            ? loc.Get("gamepad.connected", deviceName)
            : loc.Get("gamepad.disconnected", deviceName);

        trayIcon.ShowBalloonTip(
            3000,
            loc.Get("app.title"),
            message);
    }

    private async void ShowReconnectNotificationAfterDelay(
        string deviceName)
    {
        await Task.Delay(3500);
        ShowConnectionNotificationWithName(deviceName, true);
    }

    private static string GetVirtualDeviceDisplayName(
        VirtualControllerType type)
    {
        return type switch
        {
            VirtualControllerType.Xbox360 => "Xbox 360",
            _ => "DualShock 4"
        };
    }

    private void CheckViGEmBus()
    {
        var loc = LocalizationService.Instance;

        if (SystemComponentService.IsViGEmBusInstalled())
        {
            ViGEmStatusBadge.Background =
                new SolidColorBrush(Color.FromRgb(26, 58, 26));

            ViGEmStatusText.Text = loc.Get("advanced.installed");
            ViGEmStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(76, 175, 80));

            ViGEmActionButton.Visibility =
                Visibility.Collapsed;
        }
        else
        {
            ViGEmStatusBadge.Background =
                new SolidColorBrush(Color.FromRgb(58, 26, 26));

            ViGEmStatusText.Text = loc.Get("advanced.not_installed");
            ViGEmStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(244, 67, 54));

            ViGEmActionButton.Content = loc.Get("advanced.install_btn");
            ViGEmActionButton.Visibility =
                Visibility.Visible;
        }
    }

    private void CheckHidHide()
    {
        var loc = LocalizationService.Instance;

        if (SystemComponentService.IsHidHideInstalled())
        {
            HidHideStatusBadge.Background =
                new SolidColorBrush(Color.FromRgb(26, 58, 26));

            HidHideStatusText.Text = loc.Get("advanced.installed");
            HidHideStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(76, 175, 80));

            HidHideActionButton.Content = loc.Get("advanced.settings_btn");
            HidHideActionButton.Visibility =
                Visibility.Visible;
        }
        else
        {
            SetHidHideNotInstalled();
        }
    }

    private void SetHidHideNotInstalled()
    {
        var loc = LocalizationService.Instance;

        HidHideStatusBadge.Background =
            new SolidColorBrush(Color.FromRgb(58, 26, 26));

        HidHideStatusText.Text = loc.Get("advanced.not_installed");
        HidHideStatusText.Foreground =
            new SolidColorBrush(Color.FromRgb(244, 67, 54));

        HidHideActionButton.Content = loc.Get("advanced.install_btn");
        HidHideActionButton.Visibility =
            Visibility.Visible;
    }

    private void ApplySettingsToUi()
    {
        UpdateToggleButton(
            StartupToggleButton,
            activeProfile.RunAtStartup);

        UpdateToggleButton(
            TrayToggleButton,
            activeProfile.MinimizeToTray);

        UpdateToggleButton(
            NotificationsToggleButton,
            activeProfile.ShowConnectionNotifications);
    }

    private static void UpdateToggleButton(
        Button button,
        bool isEnabled)
    {
        var loc = LocalizationService.Instance;

        button.Content = isEnabled
            ? loc.Get("toggle.on")
            : loc.Get("toggle.off");

        button.Opacity = isEnabled ? 1.0 : 0.65;
    }

    private void CreateNotifyIcon()
    {
        string iconPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "appicon.ico");

        if (!File.Exists(iconPath))
        {
            try
            {
                var uri = new Uri(
                    "pack://application:,,,/assets/appicon.ico",
                    UriKind.Absolute);

                var streamInfo =
                    Application.GetResourceStream(uri);

                if (streamInfo?.Stream != null)
                {
                    using var stream = streamInfo.Stream;
                    using var fs = File.Create(iconPath);
                    stream.CopyTo(fs);
                }
            }
            catch
            {
            }
        }

        if (!File.Exists(iconPath))
            return;

        var icon = new System.Drawing.Icon(iconPath);

        trayIcon = new TrayIconService(this, icon);
        trayIcon.Visible = true;

        trayIcon.ShowRequested += () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        trayIcon.ExitRequested += () =>
        {
            if (!ControlsPage.TryCloseKeyBindingsWindow())
                return;

            isReallyClosing = true;
            Show();
            Close();
            Application.Current.Shutdown();
        };
    }

    private void StartupToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        activeProfile.RunAtStartup = !activeProfile.RunAtStartup;
        settingsService.SetRunAtStartup(activeProfile.RunAtStartup);
        SaveActiveProfile();

        UpdateToggleButton(
            StartupToggleButton,
            activeProfile.RunAtStartup);
    }

    private void TrayToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        activeProfile.MinimizeToTray = !activeProfile.MinimizeToTray;
        SaveActiveProfile();

        UpdateToggleButton(
            TrayToggleButton,
            activeProfile.MinimizeToTray);

        if (trayIcon != null)
        {
            trayIcon.Visible = true;
        }
    }

    private void NotificationsToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        activeProfile.ShowConnectionNotifications =
            !activeProfile.ShowConnectionNotifications;

        SaveActiveProfile();

        UpdateToggleButton(
            NotificationsToggleButton,
            activeProfile.ShowConnectionNotifications);
    }

    private void TurkishLangButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetLanguage("TR");
    }

    private void EnglishLangButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetLanguage("EN");
    }

    private void SetLanguage(string lang)
    {
        activeProfile.Language = lang;
        appSettings.Language = lang;
        settingsService.SaveSettings(appSettings);
        SaveActiveProfile();

        LocalizationService.Instance.SetLanguage(lang);

        UpdateLangButtonStyles();
    }

    private void UpdateLangButtonStyles()
    {
        bool isTR = activeProfile.Language == "TR";

        SetSegmentButtonActive(TurkishLangButton, isTR);
        SetSegmentButtonActive(EnglishLangButton, !isTR);
    }

    private static void SetSegmentButtonActive(Button button, bool active)
    {
        button.Foreground = active
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(160, 160, 160));

        button.Background = active
            ? new SolidColorBrush(Color.FromRgb(37, 37, 37))
            : new SolidColorBrush(Color.FromRgb(21, 21, 21));
    }

    private void ViGEmActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowComponentInstallerWindow();
    }

    private void ManageComponentsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowComponentInstallerWindow();
    }

    private void HidHideActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SystemComponentService.IsHidHideInstalled())
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
        else
        {
            ShowComponentInstallerWindow();
        }
    }

    private void ShowComponentInstallerWindow()
    {
        var installerWindow = new ComponentInstallerWindow
        {
            Owner = this
        };

        installerWindow.ShowDialog();
        CheckSystemComponents();
    }

    private void ResetSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;

        var result = MessageBox.Show(
            loc.Get("advanced.reset_confirm"),
            loc.Get("advanced.reset_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        settingsService.SetRunAtStartup(false);

        activeProfile.RunAtStartup = false;
        activeProfile.MinimizeToTray = false;
        activeProfile.ShowConnectionNotifications = true;

        SaveActiveProfile();

        ApplySettingsToUi();

        if (trayIcon != null)
        {
            trayIcon.Visible = true;
        }
    }

    // ============================================
    // GÜNCELLEMELER
    // ============================================

    private async void StartSilentUpdateCheck()
    {
        try
        {
            UpdateCheckResult result =
                await UpdateService.Instance.CheckForUpdatesAsync();

            if (result.Status != UpdateCheckStatus.UpdateAvailable)
                return;

            if (Dispatcher.HasShutdownStarted)
                return;

            ShowUpdateAvailableDialog(result);
        }
        catch
        {
        }
    }

    private async void CheckUpdatesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;

        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = loc.Get("updates.checking");

        try
        {
            UpdateCheckResult result =
                await UpdateService.Instance.CheckForUpdatesAsync();

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    ShowUpdateAvailableDialog(result);
                    break;

                case UpdateCheckStatus.UpToDate:
                    MessageBox.Show(
                        loc.Get("updates.up_to_date"),
                        loc.Get("app.title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    break;

                default:
                    MessageBox.Show(
                        loc.Get(
                            "updates.error",
                            result.ErrorMessage ?? ""),
                        loc.Get("updates.available_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = loc.Get("updates.check");
        }
    }

    private void ShowUpdateAvailableDialog(
        UpdateCheckResult result)
    {
        var dialog = new UpdateAvailableWindow(result);

        if (IsVisible)
            dialog.Owner = this;

        dialog.ShowDialog();
    }

    // ============================================
    // WM_RESTORE_APP mesaj işleyici
    // ============================================

    private nint WndProcHook(
        nint hwnd,
        int msg,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (msg == App.RestoreMessageId)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            handled = true;
        }

        return nint.Zero;
    }

    // ============================================
    // SEÇİLİ SEKME GÖRÜNÜMÜ
    // ============================================

    private void SelectNavigationButton(
        Button selectedButton)
    {
        GamepadNavButton.Background =
            new SolidColorBrush(
                Color.FromRgb(22, 22, 22));

        ColorNavButton.Background =
            new SolidColorBrush(
                Color.FromRgb(22, 22, 22));

        ControlsNavButton.Background =
            new SolidColorBrush(
                Color.FromRgb(22, 22, 22));

        AdvancedNavButton.Background =
            new SolidColorBrush(
                Color.FromRgb(22, 22, 22));

        GamepadNavButton.Foreground =
            new SolidColorBrush(
                Color.FromRgb(168, 168, 168));

        ColorNavButton.Foreground =
            new SolidColorBrush(
                Color.FromRgb(168, 168, 168));

        ControlsNavButton.Foreground =
            new SolidColorBrush(
                Color.FromRgb(168, 168, 168));

        AdvancedNavButton.Foreground =
            new SolidColorBrush(
                Color.FromRgb(168, 168, 168));

        selectedButton.Background =
            new SolidColorBrush(
                Color.FromRgb(37, 37, 37));

        selectedButton.Foreground =
            Brushes.White;
    }
}

