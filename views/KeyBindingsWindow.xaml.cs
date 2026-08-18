using System.Windows;
using GamepadApp.Services;

using System.ComponentModel;
using GamepadApp.Models;

namespace GamepadApp.Views
{
    public partial class KeyBindingsWindow : Window
    {
        public KeyBindingsWindow(Profile activeProfile)
        {
            InitializeComponent();
            BindingsView.LoadProfile(activeProfile);

            ApplyLocalization();

            LocalizationService.Instance.LanguageChanged +=
                LocalizationService_LanguageChanged;
            Closing += KeyBindingsWindow_Closing;
            Closed += KeyBindingsWindow_Closed;
        }

        private void KeyBindingsWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (BindingsView.IsCancelRequested ||
                !BindingsView.HasUnsavedChanges)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                LocalizationService.Instance.Get(
                    "controls.unsaved_changes_message"),
                LocalizationService.Instance.Get(
                    "controls.unsaved_changes_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void LocalizationService_LanguageChanged()
        {
            Dispatcher.Invoke(ApplyLocalization);
        }

        private void KeyBindingsWindow_Closed(
            object? sender,
            EventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -=
                LocalizationService_LanguageChanged;
            Closing -= KeyBindingsWindow_Closing;
        }

        private void ApplyLocalization()
        {
            Title = LocalizationService.Instance.Get(
                "controls.keybindings_window_title");
        }
    }
}
