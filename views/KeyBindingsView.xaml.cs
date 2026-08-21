using GamepadApp.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamepadApp.Models;

namespace GamepadApp.Views
{
    public partial class KeyBindingsView : UserControl
    {
        private readonly ProfileService profileService = new();

        private readonly DispatcherTimer deviceRefreshTimer;

        private Profile? activeProfile;
        private ControllerProfileSettings draftSettings = new();

        private bool isLoadingProfileSettings;
        private bool hasUnsavedChanges;

        public bool HasUnsavedChanges => hasUnsavedChanges;
        public bool IsCancelRequested { get; private set; }

        // Atama bekleyen kaynak tuş.
        // Örn: "Cross", "D-Pad Up", "L1", "Options"
        private string? waitingSourceButton;
        private bool bindingNeutralObserved;

        private bool isXboxMode;
        private bool isLoadingCombo;
        private bool languageEventSubscribed;

        public KeyBindingsView()
        {
            InitializeComponent();

            DeadzoneSlider.ValueChanged += AnalogSlider_ValueChanged;
            AntiDeadzoneSlider.ValueChanged += AnalogSlider_ValueChanged;
            SensitivitySlider.ValueChanged += AnalogSlider_ValueChanged;

            GamepadComboBox.SelectionChanged +=
                GamepadComboBox_SelectionChanged;

            deviceRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            deviceRefreshTimer.Tick +=
                DeviceRefreshTimer_Tick;

            Loaded += KeyBindingsView_Loaded;
            Unloaded += KeyBindingsView_Unloaded;
        }

        public void LoadProfile(Profile profile)
        {
            CancelPendingBinding(restoreButtonText: false);
            activeProfile = profile;
            draftSettings = (profile.ControllerSettings ?? new()).Clone();

            isLoadingProfileSettings = true;
            isLoadingCombo = true;

            DeadzoneSlider.Value = draftSettings.Deadzone;
            AntiDeadzoneSlider.Value = draftSettings.AntiDeadzone;
            SensitivitySlider.Value = draftSettings.Sensitivity;
            LeftMotorSlider.Value = draftSettings.LeftMotorStrength;
            RightMotorSlider.Value = draftSettings.RightMotorStrength;

            isXboxMode = string.Equals(
                draftSettings.OutputGamepadType,
                "Xbox360",
                StringComparison.OrdinalIgnoreCase);
            GamepadComboBox.SelectedIndex = isXboxMode ? 1 : 0;

            TouchpadModeComboBox.SelectedIndex =
                draftSettings.TouchpadMode switch
                {
                    TouchpadMode.Mouse => 1,
                    TouchpadMode.Disabled => 2,
                    _ => 0
                };

            ActiveProfileNameText.Text = profile.Name;
            GamepadProfileText.Text = profile.Name;
            LoadAllBindingButtons(draftSettings);
            UpdateBindingLabels();

            isLoadingCombo = false;
            isLoadingProfileSettings = false;
            SetDirty(false);
        }

        private void KeyBindingsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ApplyLocalization();

            if (!languageEventSubscribed)
            {
                LocalizationService.Instance.LanguageChanged +=
                    LocalizationService_LanguageChanged;
                languageEventSubscribed = true;
            }

            LoadConnectedGamepad();
            deviceRefreshTimer.Start();
        }

        private void KeyBindingsView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            deviceRefreshTimer.Stop();
            CancelPendingBinding(restoreButtonText: false);

            if (!languageEventSubscribed)
                return;

            LocalizationService.Instance.LanguageChanged -=
                LocalizationService_LanguageChanged;
            languageEventSubscribed = false;
        }

        private void LocalizationService_LanguageChanged()
        {
            Dispatcher.Invoke(ApplyLocalization);
        }

        private void DeviceRefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            LoadConnectedGamepad();
        }

        // ============================================
        // ANALOG AYARLARI
        // ============================================

        private void AnalogSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            DeadzoneValueText.Text =
                $"%{(int)DeadzoneSlider.Value}";

            AntiDeadzoneValueText.Text =
                $"%{(int)AntiDeadzoneSlider.Value}";

            SensitivityValueText.Text =
                $"%{(int)SensitivitySlider.Value}";

            if (isLoadingProfileSettings)
            {
                return;
            }

            draftSettings.Deadzone = DeadzoneSlider.Value;
            draftSettings.AntiDeadzone = AntiDeadzoneSlider.Value;
            draftSettings.Sensitivity = SensitivitySlider.Value;
            SetDirty(true);
        }

        // ============================================
        // TİTREŞİM DEĞERLERİ
        // ============================================

        private void LeftMotorSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (LeftMotorValueText != null)
            {
                LeftMotorValueText.Text =
                    $"{Math.Round(e.NewValue)}%";
            }

            if (!isLoadingProfileSettings)
            {
                draftSettings.LeftMotorStrength = e.NewValue;
                SetDirty(true);
            }
        }

        private void RightMotorSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (RightMotorValueText != null)
            {
                RightMotorValueText.Text =
                    $"{Math.Round(e.NewValue)}%";
            }

            if (!isLoadingProfileSettings)
            {
                draftSettings.RightMotorStrength = e.NewValue;
                SetDirty(true);
            }
        }

        // ============================================
        // TUŞ ATAMA CLICK EVENTLERİ
        // ============================================

        private void CrossBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Cross",
                CrossBindingBtn);
        }

        private void CircleBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Circle",
                CircleBindingBtn);
        }

        private void TriangleBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Triangle",
                TriangleBindingBtn);
        }

        private void SquareBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Square",
                SquareBindingBtn);
        }

        private void DpadUpBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "D-Pad Up",
                DpadUpBindingBtn);
        }

        private void DpadLeftBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "D-Pad Left",
                DpadLeftBindingBtn);
        }

        private void DpadRightBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "D-Pad Right",
                DpadRightBindingBtn);
        }

        private void DpadDownBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "D-Pad Down",
                DpadDownBindingBtn);
        }

        private void L1BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "L1",
                L1BindingBtn);
        }

        private void R1BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "R1",
                R1BindingBtn);
        }

        private void L2BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "L2",
                L2BindingBtn);
        }

        private void R2BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "R2",
                R2BindingBtn);
        }

        private void L3BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "L3",
                L3BindingBtn);
        }

        private void R3BindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "R3",
                R3BindingBtn);
        }

        private void ShareBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Share",
                ShareBindingBtn);
        }

        private void OptionsBindingBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            BeginBinding(
                "Options",
                OptionsBindingBtn);
        }

        private void LeftStickUpBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("LS Y+", LeftStickUpBindingBtn);

        private void LeftStickDownBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("LS Y-", LeftStickDownBindingBtn);

        private void LeftStickLeftBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("LS X-", LeftStickLeftBindingBtn);

        private void LeftStickRightBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("LS X+", LeftStickRightBindingBtn);

        private void RightStickUpBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("RS Y+", RightStickUpBindingBtn);

        private void RightStickDownBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("RS Y-", RightStickDownBindingBtn);

        private void RightStickLeftBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("RS X-", RightStickLeftBindingBtn);

        private void RightStickRightBindingBtn_Click(
            object sender,
            RoutedEventArgs e) =>
            BeginBinding("RS X+", RightStickRightBindingBtn);

        private void BeginBinding(
            string sourceButton,
            Button sourceButtonControl)
        {
            ReleaseAllVirtualState();

            waitingSourceButton =
                sourceButton;
            bindingNeutralObserved = false;

            sourceButtonControl.Content =
                LocalizationService.Instance.Get(
                    "controls.press_button");

            deviceRefreshTimer.Interval =
                TimeSpan.FromMilliseconds(30);
        }

        private void CancelPendingBinding(bool restoreButtonText = true)
        {
            if (waitingSourceButton == null)
                return;

            waitingSourceButton = null;
            bindingNeutralObserved = false;
            deviceRefreshTimer.Interval = TimeSpan.FromMilliseconds(500);

            if (restoreButtonText)
                LoadAllBindingButtons(draftSettings);
        }

        // ============================================
        // PROFİLDEN TÜM TUŞLARI YÜKLE
        // ============================================

        private void LoadAllBindingButtons(
            ControllerProfileSettings settings)
        {
            LoadBindingButton(
                settings,
                "Cross",
                CrossBindingBtn);

            LoadBindingButton(
                settings,
                "Circle",
                CircleBindingBtn);

            LoadBindingButton(
                settings,
                "Triangle",
                TriangleBindingBtn);

            LoadBindingButton(
                settings,
                "Square",
                SquareBindingBtn);

            LoadBindingButton(
                settings,
                "D-Pad Up",
                DpadUpBindingBtn);

            LoadBindingButton(
                settings,
                "D-Pad Left",
                DpadLeftBindingBtn);

            LoadBindingButton(
                settings,
                "D-Pad Right",
                DpadRightBindingBtn);

            LoadBindingButton(
                settings,
                "D-Pad Down",
                DpadDownBindingBtn);

            LoadBindingButton(
                settings,
                "L1",
                L1BindingBtn);

            LoadBindingButton(
                settings,
                "R1",
                R1BindingBtn);

            LoadBindingButton(
                settings,
                "L2",
                L2BindingBtn);

            LoadBindingButton(
                settings,
                "R2",
                R2BindingBtn);

            LoadBindingButton(
                settings,
                "L3",
                L3BindingBtn);

            LoadBindingButton(
                settings,
                "R3",
                R3BindingBtn);

            LoadBindingButton(
                settings,
                "Share",
                ShareBindingBtn);

            LoadBindingButton(
                settings,
                "Options",
                OptionsBindingBtn);

            LoadBindingButton(settings, "LS Y+", LeftStickUpBindingBtn);
            LoadBindingButton(settings, "LS Y-", LeftStickDownBindingBtn);
            LoadBindingButton(settings, "LS X-", LeftStickLeftBindingBtn);
            LoadBindingButton(settings, "LS X+", LeftStickRightBindingBtn);
            LoadBindingButton(settings, "RS Y+", RightStickUpBindingBtn);
            LoadBindingButton(settings, "RS Y-", RightStickDownBindingBtn);
            LoadBindingButton(settings, "RS X-", RightStickLeftBindingBtn);
            LoadBindingButton(settings, "RS X+", RightStickRightBindingBtn);
        }

        private void LoadBindingButton(
            ControllerProfileSettings settings,
            string sourceButton,
            Button targetButton)
        {
            if (settings.ButtonMappings != null &&
                settings.ButtonMappings.TryGetValue(
                    sourceButton,
                    out string? mappedButton))
            {
                targetButton.Content =
                    GetDisplayButtonName(mappedButton);
            }
            else
            {
                targetButton.Content =
                    GetDisplayButtonName(sourceButton);
            }
        }

        private void ResetAllBindingButtons()
        {
            LoadAllBindingButtons(new ControllerProfileSettings());
        }

        // ============================================
        // TİTREŞİM TESTİ
        // ============================================

        private async void VibrationTestBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            var mainWindow = Application.Current.Windows
                .OfType<MainWindow>()
                .FirstOrDefault();

            if (mainWindow?.EmulationService?.CurrentDescriptor?
                    .SupportsRumble != true)
            {
                return;
            }

            byte leftMotor =
                (byte)(
                    LeftMotorSlider.Value /
                    100.0 *
                    255);

            byte rightMotor =
                (byte)(
                    RightMotorSlider.Value /
                    100.0 *
                    255);

            mainWindow.EmulationService.TrySetVibration(
                leftMotor,
                rightMotor,
                durationMs: 500);

            await Task.Delay(500);

            mainWindow.EmulationService.TrySetVibration(0, 0, 0);
        }

        // ============================================
        // GAMEPAD DURUMU
        // ============================================

        private void LoadConnectedGamepad()
        {
            DeviceComboBox.Items.Clear();

            var mainWindow = Application.Current.Windows
                .OfType<MainWindow>()
                .FirstOrDefault();

            PhysicalGamepadDescriptor? descriptor =
                mainWindow?.EmulationService?.CurrentDescriptor;
            PhysicalGamepadState? input =
                mainWindow?.EmulationService?.CurrentInput;

            if (descriptor == null || input?.IsConnected != true)
            {
                CancelPendingBinding();
                ReleaseAllVirtualState();

                var loc = LocalizationService.Instance;

                DeviceComboBox.Items.Add(
                    loc.Get("gamepad.no_controller"));

                DeviceComboBox.SelectedIndex = 0;

                GamepadStatusText.Text =
                    loc.Get("controls.not_connected");

                GamepadDeviceText.Text =
                    "-";

                GamepadConnectionText.Text =
                    "-";

                GamepadBatteryText.Text = "-";
                GamepadProfileText.Text =
                    activeProfile?.Name ?? "-";
                ActiveProfileNameText.Text =
                    activeProfile?.Name ?? "-";

                BindingsContent.IsEnabled = false;
                BindingsContent.Opacity = 0.45;
                ResetDefaultsBtn.IsEnabled = false;

                return;
            }

            BindingsContent.IsEnabled = true;
            BindingsContent.Opacity = 1.0;
            ResetDefaultsBtn.IsEnabled = true;

            DeviceComboBox.Items.Add(
                $"{descriptor.DisplayName} - " +
                descriptor.ConnectionDisplayName);

            DeviceComboBox.SelectedIndex = 0;

            GamepadStatusText.Text = LocalizationService.Instance.Get(
                "controls.connected");
            GamepadDeviceText.Text = descriptor.DisplayName;
            GamepadConnectionText.Text =
                descriptor.ConnectionDisplayName;

            int? batteryPercentage = input.BatteryPercentage;
            GamepadBatteryText.Text = batteryPercentage.HasValue
                ? $"%{batteryPercentage.Value}"
                : LocalizationService.Instance.Get("controls.unavailable");

            CaptureBindingInput(input);

            GamepadProfileText.Text = activeProfile?.Name ?? "-";
            ActiveProfileNameText.Text = activeProfile?.Name ?? "-";
        }

        private void ReleaseAllVirtualState()
        {
            var mainWindow =
                Application.Current.Windows
                    .OfType<MainWindow>()
                    .FirstOrDefault();

            mainWindow?.VirtualGamepadService.ResetState();
        }

        private void CaptureBindingInput(
            PhysicalGamepadState input)
        {
            if (waitingSourceButton == null)
            {
                return;
            }

            string? pressedButton =
                GetPressedButton(input);

            if (!bindingNeutralObserved)
            {
                if (pressedButton == null)
                    bindingNeutralObserved = true;

                return;
            }

            if (pressedButton == null)
                return;

            string sourceButton =
                waitingSourceButton;

            Button? sourceButtonControl =
                GetBindingButton(
                    sourceButton);

            if (sourceButtonControl != null)
            {
                sourceButtonControl.Content =
                    GetDisplayButtonName(pressedButton);
            }

            waitingSourceButton = null;

            deviceRefreshTimer.Interval =
                TimeSpan.FromMilliseconds(500);

            SaveButtonMapping(
                sourceButton,
                pressedButton);
        }

        // ============================================
        // ORTAK FİZİKSEL STATE'TEN BASILAN TUŞU BUL
        // ============================================

        private string? GetPressedButton(
            PhysicalGamepadState input)
        {
            string[] buttonPriority =
            [
                "D-Pad Up",
                "D-Pad Right",
                "D-Pad Down",
                "D-Pad Left",
                "Square",
                "Cross",
                "Circle",
                "Triangle",
                "L1",
                "R1",
                "Share",
                "Options",
                "L3",
                "R3"
            ];

            foreach (string button in buttonPriority)
            {
                if (input.Buttons.Contains(button))
                    return button;
            }

            if (input.LeftTrigger > 30)
                return "L2";

            if (input.RightTrigger > 30)
                return "R2";

            const int stickThreshold = 64;

            if (input.LeftStickY <= 128 - stickThreshold)
                return "LS Y+";
            if (input.LeftStickY >= 128 + stickThreshold)
                return "LS Y-";
            if (input.LeftStickX <= 128 - stickThreshold)
                return "LS X-";
            if (input.LeftStickX >= 128 + stickThreshold)
                return "LS X+";
            if (input.RightStickY <= 128 - stickThreshold)
                return "RS Y+";
            if (input.RightStickY >= 128 + stickThreshold)
                return "RS Y-";
            if (input.RightStickX <= 128 - stickThreshold)
                return "RS X-";
            if (input.RightStickX >= 128 + stickThreshold)
                return "RS X+";

            return null;
        }

        // ============================================
        // ATAMAYI PROFİLE KAYDET
        // ============================================

        private void SaveButtonMapping(
            string sourceButton,
            string pressedButton)
        {
            draftSettings.ButtonMappings[sourceButton] = pressedButton;
            SetDirty(true);
        }

        // ============================================
        // ALT BAR
        // ============================================

        private void ResetToDefaultsBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!BindingsContent.IsEnabled)
                return;

            CancelPendingBinding(restoreButtonText: false);
            ReleaseAllVirtualState();

            isLoadingProfileSettings = true;

            draftSettings = new ControllerProfileSettings
            {
                OutputGamepadType = isXboxMode
                    ? "Xbox360"
                    : "DualShock4"
            };

            DeadzoneSlider.Value = draftSettings.Deadzone;
            AntiDeadzoneSlider.Value = draftSettings.AntiDeadzone;
            SensitivitySlider.Value = draftSettings.Sensitivity;
            LeftMotorSlider.Value = draftSettings.LeftMotorStrength;
            RightMotorSlider.Value = draftSettings.RightMotorStrength;
            TouchpadModeComboBox.SelectedIndex = 0;

            ResetAllBindingButtons();

            UpdateBindingLabels();

            isLoadingProfileSettings = false;
            SetDirty(true);
        }

        private void SaveBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (activeProfile == null)
            {
                return;
            }

            CancelPendingBinding();

            activeProfile.ControllerSettings = draftSettings.Clone();
            activeProfile.ControllerSettingsInitialized = true;
            profileService.SaveProfile(activeProfile);

            GamepadService.DeadzonePercent = draftSettings.Deadzone;
            GamepadService.AntiDeadzonePercent = draftSettings.AntiDeadzone;
            GamepadService.SensitivityPercent = draftSettings.Sensitivity;
            GamepadService.SelectedVirtualType = isXboxMode
                ? VirtualControllerType.Xbox360
                : VirtualControllerType.DualShock4;

            draftSettings = activeProfile.ControllerSettings.Clone();
            SetDirty(false);
        }

        private void CancelBtn_Click(
            object sender,
            RoutedEventArgs e)
        {
            IsCancelRequested = true;
            CancelPendingBinding(restoreButtonText: false);
            ReleaseAllVirtualState();

            Window? hostWindow =
                Window.GetWindow(this);

            hostWindow?.Close();
        }

        // ============================================
        // KAYNAK TUŞTAN UI BUTONUNU BUL
        // ============================================

        private Button? GetBindingButton(
            string sourceButton)
        {
            return sourceButton switch
            {
                "Cross" =>
                    CrossBindingBtn,

                "Circle" =>
                    CircleBindingBtn,

                "Triangle" =>
                    TriangleBindingBtn,

                "Square" =>
                    SquareBindingBtn,

                "D-Pad Up" =>
                    DpadUpBindingBtn,

                "D-Pad Left" =>
                    DpadLeftBindingBtn,

                "D-Pad Right" =>
                    DpadRightBindingBtn,

                "D-Pad Down" =>
                    DpadDownBindingBtn,

                "L1" =>
                    L1BindingBtn,

                "R1" =>
                    R1BindingBtn,

                "L2" =>
                    L2BindingBtn,

                "R2" =>
                    R2BindingBtn,

                "L3" =>
                    L3BindingBtn,

                "R3" =>
                    R3BindingBtn,

                "Share" =>
                    ShareBindingBtn,

                "Options" =>
                    OptionsBindingBtn,

                "LS Y+" => LeftStickUpBindingBtn,
                "LS Y-" => LeftStickDownBindingBtn,
                "LS X-" => LeftStickLeftBindingBtn,
                "LS X+" => LeftStickRightBindingBtn,
                "RS Y+" => RightStickUpBindingBtn,
                "RS Y-" => RightStickDownBindingBtn,
                "RS X-" => RightStickLeftBindingBtn,
                "RS X+" => RightStickRightBindingBtn,

                _ => null
            };
        }

        private void GamepadComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoadingCombo)
                return;

            if (GamepadComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                CancelPendingBinding();
                isXboxMode = tag == "Xbox360";
                draftSettings.OutputGamepadType = isXboxMode
                    ? "Xbox360"
                    : "DualShock4";
                UpdateBindingLabels();
                SetDirty(true);
            }
        }

        private void TouchpadModeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoadingCombo)
                return;

            if (TouchpadModeComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                draftSettings.TouchpadMode = ParseTouchpadMode(tag);
                SetDirty(true);
            }
        }

        private static TouchpadMode ParseTouchpadMode(string? tag)
        {
            return tag switch
            {
                "Mouse" => TouchpadMode.Mouse,
                "Disabled" => TouchpadMode.Disabled,
                _ => TouchpadMode.Normal
            };
        }

        private void SetDirty(bool isDirty)
        {
            hasUnsavedChanges = isDirty;
            if (SaveBtn != null)
            {
                SaveBtn.IsEnabled = isDirty;
            }
        }

        private void UpdateBindingLabels()
        {
            string layoutImage = isXboxMode
                ? "/assets/controller2_bindings_layout.png"
                : "/assets/controller_bindings_layout.png";

            BindingsLayoutImage.Source = new BitmapImage(
                new Uri($"pack://application:,,,{layoutImage}", UriKind.Absolute));

            CrossSourceLabel.Text = GetDisplayButtonName("Cross");
            CircleSourceLabel.Text = GetDisplayButtonName("Circle");
            SquareSourceLabel.Text = GetDisplayButtonName("Square");
            TriangleSourceLabel.Text = GetDisplayButtonName("Triangle");
            L1SourceLabel.Text = GetDisplayButtonName("L1");
            R1SourceLabel.Text = GetDisplayButtonName("R1");
            L2SourceLabel.Text = GetDisplayButtonName("L2");
            R2SourceLabel.Text = GetDisplayButtonName("R2");
            L3SourceLabel.Text = GetDisplayButtonName("L3");
            R3SourceLabel.Text = GetDisplayButtonName("R3");
            ShareSourceLabel.Text = GetDisplayButtonName("Share");
            OptionsSourceLabel.Text = GetDisplayButtonName("Options");

            DpadUpLabel.Text = isXboxMode ? "D-Pad Up" : GetLocalized("controls.up");
            DpadDownLabel.Text = isXboxMode ? "D-Pad Down" : GetLocalized("controls.down");
            DpadLeftLabel.Text = isXboxMode ? "D-Pad Left" : GetLocalized("controls.left");
            DpadRightLabel.Text = isXboxMode ? "D-Pad Right" : GetLocalized("controls.right");

            LeftStickUpLabel.Text = GetLocalized("controls.up");
            LeftStickDownLabel.Text = GetLocalized("controls.down");
            LeftStickLeftLabel.Text = GetLocalized("controls.left");
            LeftStickRightLabel.Text = GetLocalized("controls.right");

            RightStickUpLabel.Text = GetLocalized("controls.up");
            RightStickDownLabel.Text = GetLocalized("controls.down");
            RightStickLeftLabel.Text = GetLocalized("controls.left");
            RightStickRightLabel.Text = GetLocalized("controls.right");

            LoadAllBindingButtons(draftSettings);
        }

        private string GetDisplayButtonName(string buttonName)
        {
            if (!isXboxMode)
                return buttonName;

            return buttonName switch
            {
                "Cross" => "A",
                "Circle" => "B",
                "Square" => "X",
                "Triangle" => "Y",
                "L1" => "LB",
                "R1" => "RB",
                "L2" => "LT",
                "R2" => "RT",
                "L3" => "LS",
                "R3" => "RS",
                "Share" => "Back",
                "Options" => "Start",
                _ => buttonName
            };
        }

        private string GetLocalized(string key)
        {
            return LocalizationService.Instance.Get(key);
        }

        private void ApplyLocalization()
        {
            var loc = LocalizationService.Instance;

            GamepadSectionTitle.Text = loc.Get("nav.gamepad");
            DeviceSectionTitle.Text = loc.Get("controls.device_label");
            TouchpadModeSectionTitle.Text = loc.Get("controls.touchpad_mode");
            ActiveProfileSectionTitle.Text = loc.Get("controls.active_profile");

            TouchpadNormalItem.Content = loc.Get("controls.touchpad_normal");
            TouchpadMouseItem.Content = loc.Get("controls.touchpad_mouse");
            TouchpadDisabledItem.Content = loc.Get("controls.touchpad_disabled");

            DpadSectionTitle.Text = loc.Get("controls.dpad");
            DpadUpLabel.Text = loc.Get("controls.up");
            DpadLeftLabel.Text = loc.Get("controls.left");
            DpadRightLabel.Text = loc.Get("controls.right");
            DpadDownLabel.Text = loc.Get("controls.down");

            LeftStickSectionTitle.Text = loc.Get("controls.left_stick");
            LeftStickUpLabel.Text = loc.Get("controls.up");
            LeftStickLeftLabel.Text = loc.Get("controls.left");
            LeftStickRightLabel.Text = loc.Get("controls.right");
            LeftStickDownLabel.Text = loc.Get("controls.down");

            CenterSectionTitle.Text = loc.Get("nav.gamepad");
            FaceButtonsSectionTitle.Text = loc.Get("controls.face_buttons");

            RightStickSectionTitle.Text = loc.Get("controls.right_stick");
            RightStickUpLabel.Text = loc.Get("controls.up");
            RightStickLeftLabel.Text = loc.Get("controls.left");
            RightStickRightLabel.Text = loc.Get("controls.right");
            RightStickDownLabel.Text = loc.Get("controls.down");

            VibrationSectionTitle.Text = loc.Get("controls.vibration_triggers");
            VibrationTestBtn.Content = loc.Get("controls.test_vibration");
            LeftMotorLabel.Text = loc.Get("controls.left_motor");
            RightMotorLabel.Text = loc.Get("controls.right_motor");

            GamepadInfoSectionTitle.Text = loc.Get("controls.gamepad_info");
            StatusLabelText.Text = loc.Get("controls.status");
            DeviceLabelText.Text = loc.Get("controls.device");
            ConnectionLabelText.Text = loc.Get("controls.connection");
            BatteryLabelText.Text = loc.Get("controls.battery");
            ProfileLabelText.Text = loc.Get("controls.profile");

            AnalogSettingsSectionTitle.Text = loc.Get("controls.analog_settings");
            DeadzoneLabel.Text = loc.Get("controls.deadzone");
            AntiDeadzoneLabel.Text = loc.Get("controls.anti_deadzone");
            SensitivityLabel.Text = loc.Get("controls.sensitivity");

            ResetDefaultsBtn.Content = loc.Get("controls.reset_defaults");
            SaveBtn.Content = loc.Get("controls.save");
            CancelBtn.Content = loc.Get("profile.cancel");

            UpdateBindingLabels();
        }
    }
}
