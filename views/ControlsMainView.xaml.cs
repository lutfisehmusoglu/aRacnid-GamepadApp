using System.Windows;
using System.Windows.Controls;
using GamepadApp.Services;

namespace GamepadApp.Views
{
    public partial class ControlsMainView : UserControl
    {
        private KeyBindingsWindow? keyBindingsWindow;
        private bool eventsSubscribed;

        public ControlsMainView()
        {
            InitializeComponent();

            Loaded += ControlsMainView_Loaded;
            Unloaded += ControlsMainView_Unloaded;
        }

        private void ControlsMainView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ApplyLocalization();

            bool isXbox =
                GamepadService.SelectedVirtualType ==
                VirtualControllerType.Xbox360;
            TesterPage.SetGamepadMode(isXbox);

            if (eventsSubscribed)
                return;

            LocalizationService.Instance.LanguageChanged +=
                LocalizationService_LanguageChanged;
            GamepadService.SelectedVirtualTypeChanged +=
                GamepadService_SelectedVirtualTypeChanged;
            eventsSubscribed = true;
        }

        private void ControlsMainView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!eventsSubscribed)
                return;

            LocalizationService.Instance.LanguageChanged -=
                LocalizationService_LanguageChanged;
            GamepadService.SelectedVirtualTypeChanged -=
                GamepadService_SelectedVirtualTypeChanged;
            eventsSubscribed = false;
        }

        private void LocalizationService_LanguageChanged()
        {
            Dispatcher.Invoke(ApplyLocalization);
        }

        private void GamepadService_SelectedVirtualTypeChanged(
            VirtualControllerType type)
        {
            Dispatcher.Invoke(() => TesterPage.SetGamepadMode(
                type == VirtualControllerType.Xbox360));
        }

        private void ApplyLocalization()
        {
            OpenKeyBindingsBtn.Content = LocalizationService.Instance.Get(
                "controls.keybindings_btn");
        }

        private void OpenKeyBindingsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (keyBindingsWindow != null)
            {
                if (!keyBindingsWindow.IsVisible)
                    keyBindingsWindow.Show();

                if (keyBindingsWindow.WindowState ==
                    WindowState.Minimized)
                {
                    keyBindingsWindow.WindowState =
                        WindowState.Normal;
                }

                keyBindingsWindow.Activate();
                keyBindingsWindow.Focus();
                return;
            }

            MainWindow? mainWindow =
                Window.GetWindow(this) as MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            keyBindingsWindow = new KeyBindingsWindow(
                mainWindow.ActiveProfile)
            {
                Owner = mainWindow
            };

            keyBindingsWindow.Closed += KeyBindingsWindow_Closed;
            keyBindingsWindow.Show();
        }

        private void KeyBindingsWindow_Closed(
            object? sender,
            EventArgs e)
        {
            if (keyBindingsWindow != null)
            {
                keyBindingsWindow.Closed -=
                    KeyBindingsWindow_Closed;
                keyBindingsWindow = null;
            }
        }

        public bool TryCloseKeyBindingsWindow()
        {
            if (keyBindingsWindow == null)
                return true;

            keyBindingsWindow.Close();

            // Closing olayı kaydedilmemiş değişiklikler nedeniyle iptal
            // edildiyse pencere referansı Closed tarafından temizlenmez.
            return keyBindingsWindow == null;
        }
    }
}
