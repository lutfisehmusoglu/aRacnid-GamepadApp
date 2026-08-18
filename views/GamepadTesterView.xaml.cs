using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamepadApp.Models;
using GamepadApp.Services;

namespace GamepadApp.Views
{
    public partial class GamepadTesterView : UserControl
    {
        private readonly GamepadService gamepadService =
            new GamepadService();

        private readonly ButtonRemapService buttonRemapService =
            new ButtonRemapService();

        private readonly DispatcherTimer pollTimer;

        private bool isXboxMode;

        public GamepadTesterView()
        {
            InitializeComponent();

            pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            pollTimer.Tick += PollTimer_Tick;

            Loaded += (s, e) => pollTimer.Start();
            Unloaded += (s, e) => pollTimer.Stop();
        }

        public void SetGamepadMode(bool xboxMode)
        {
            isXboxMode = xboxMode;

            string imagePath = xboxMode
                ? "/assets/controller2_tester_base.png"
                : "/assets/controller_tester_base.png";

            var uri = new Uri($"pack://application:,,,{imagePath}", UriKind.Absolute);
            var bmp = new BitmapImage(uri);

            BaseControllerImage.Source = bmp;

            Canvas.SetLeft(XboxHighlightLayer, xboxMode ? 10 : 0);

            Ds4HighlightLayer.Visibility =
                xboxMode
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            XboxHighlightLayer.Visibility =
                xboxMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (xboxMode)
            {
                ControllerSymbolOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                ControllerSymbolOverlay.Visibility = Visibility.Visible;
                ControllerSymbolOverlay.Source = bmp;
            }
        }

        private void PollTimer_Tick(
            object? sender,
            EventArgs e)
        {
            var mainWindow =
                Application.Current.Windows
                    .OfType<MainWindow>()
                    .FirstOrDefault();

            var input = mainWindow?.EmulationService?.CurrentInput;

            if (input == null || !input.IsConnected)
            {
                ResetHighlights();
                return;
            }

            GamepadOutputState mappedOutput =
    mainWindow!.EmulationService!.BuildOutputState(input);

            HashSet<string> outputButtons = mappedOutput.Buttons;

            double squareOpacity =
                outputButtons.Contains("Square") ? 1.0 : 0.0;
            double crossOpacity =
                outputButtons.Contains("Cross") ? 1.0 : 0.0;
            double circleOpacity =
                outputButtons.Contains("Circle") ? 1.0 : 0.0;
            double triangleOpacity =
                outputButtons.Contains("Triangle") ? 1.0 : 0.0;
            double dpadUpOpacity =
                outputButtons.Contains("D-Pad Up") ? 1.0 : 0.0;
            double dpadRightOpacity =
                outputButtons.Contains("D-Pad Right") ? 1.0 : 0.0;
            double dpadDownOpacity =
                outputButtons.Contains("D-Pad Down") ? 1.0 : 0.0;
            double dpadLeftOpacity =
                outputButtons.Contains("D-Pad Left") ? 1.0 : 0.0;
            double l1Opacity =
                outputButtons.Contains("L1") ? 1.0 : 0.0;
            double r1Opacity =
                outputButtons.Contains("R1") ? 1.0 : 0.0;
            double l3Opacity =
                outputButtons.Contains("L3") ? 1.0 : 0.0;
            double r3Opacity =
                outputButtons.Contains("R3") ? 1.0 : 0.0;
            double shareOpacity =
                outputButtons.Contains("Share") ? 1.0 : 0.0;
            double optionsOpacity =
                outputButtons.Contains("Options") ? 1.0 : 0.0;

            SquareHighlight.Opacity = squareOpacity;
            CrossHighlight.Opacity = crossOpacity;
            CircleHighlight.Opacity = circleOpacity;
            TriangleHighlight.Opacity = triangleOpacity;
            DpadUpHighlight.Opacity = dpadUpOpacity;
            DpadRightHighlight.Opacity = dpadRightOpacity;
            DpadDownHighlight.Opacity = dpadDownOpacity;
            DpadLeftHighlight.Opacity = dpadLeftOpacity;
            L1Highlight.Opacity = l1Opacity;
            R1Highlight.Opacity = r1Opacity;
            L3Highlight.Opacity = l3Opacity;
            R3Highlight.Opacity = r3Opacity;
            ShareHighlight.Opacity = shareOpacity;
            OptionsHighlight.Opacity = optionsOpacity;

            XboxSquareHighlight.Opacity = squareOpacity;
            XboxCrossHighlight.Opacity = crossOpacity;
            XboxCircleHighlight.Opacity = circleOpacity;
            XboxTriangleHighlight.Opacity = triangleOpacity;
            XboxDpadUpHighlight.Opacity = dpadUpOpacity;
            XboxDpadRightHighlight.Opacity = dpadRightOpacity;
            XboxDpadDownHighlight.Opacity = dpadDownOpacity;
            XboxDpadLeftHighlight.Opacity = dpadLeftOpacity;
            XboxL1Highlight.Opacity = l1Opacity;
            XboxR1Highlight.Opacity = r1Opacity;
            XboxL3Highlight.Opacity = l3Opacity;
            XboxR3Highlight.Opacity = r3Opacity;
            XboxShareHighlight.Opacity = shareOpacity;
            XboxOptionsHighlight.Opacity = optionsOpacity;

            double leftTriggerOpacity = mappedOutput.LeftTrigger / 255.0;
            double rightTriggerOpacity = mappedOutput.RightTrigger / 255.0;

            L2Highlight.Opacity = leftTriggerOpacity;
            R2Highlight.Opacity = rightTriggerOpacity;

            XboxL2Highlight.Opacity = leftTriggerOpacity;
            XboxR2Highlight.Opacity = rightTriggerOpacity;

            PSHighlight.Opacity =
                mappedOutput.PsPressed ? 1.0 : 0.0;

            TouchpadHighlight.Opacity =
                mappedOutput.TouchpadPressed ? 1.0 : 0.0;

            XboxViewHighlight.Opacity =
                mappedOutput.TouchpadPressed ? 1.0 : 0.0;

            UpdateAnalogSticks(mappedOutput);
        }

        private void UpdateAnalogSticks(
    GamepadOutputState output)
        {
            Canvas.SetLeft(
                LeftStickDot,
                (output.LeftStickX / 255.0) * 260 - 12);

            Canvas.SetTop(
                LeftStickDot,
                (output.LeftStickY / 255.0) * 260 - 12);

            Canvas.SetLeft(
                RightStickDot,
                (output.RightStickX / 255.0) * 260 - 12);

            Canvas.SetTop(
                RightStickDot,
                (output.RightStickY / 255.0) * 260 - 12);
        }

        private void ResetHighlights()
        {
            L2Highlight.Opacity = 0;
            R2Highlight.Opacity = 0;

            L1Highlight.Opacity = 0;
            R1Highlight.Opacity = 0;

            L3Highlight.Opacity = 0;
            R3Highlight.Opacity = 0;

            DpadUpHighlight.Opacity = 0;
            DpadRightHighlight.Opacity = 0;
            DpadDownHighlight.Opacity = 0;
            DpadLeftHighlight.Opacity = 0;

            SquareHighlight.Opacity = 0;
            CrossHighlight.Opacity = 0;
            CircleHighlight.Opacity = 0;
            TriangleHighlight.Opacity = 0;

            ShareHighlight.Opacity = 0;
            OptionsHighlight.Opacity = 0;
            PSHighlight.Opacity = 0;
            TouchpadHighlight.Opacity = 0;

            XboxL2Highlight.Opacity = 0;
            XboxR2Highlight.Opacity = 0;

            XboxL1Highlight.Opacity = 0;
            XboxR1Highlight.Opacity = 0;

            XboxL3Highlight.Opacity = 0;
            XboxR3Highlight.Opacity = 0;

            XboxDpadUpHighlight.Opacity = 0;
            XboxDpadRightHighlight.Opacity = 0;
            XboxDpadDownHighlight.Opacity = 0;
            XboxDpadLeftHighlight.Opacity = 0;

            XboxSquareHighlight.Opacity = 0;
            XboxCrossHighlight.Opacity = 0;
            XboxCircleHighlight.Opacity = 0;
            XboxTriangleHighlight.Opacity = 0;

            XboxShareHighlight.Opacity = 0;
            XboxOptionsHighlight.Opacity = 0;
            XboxViewHighlight.Opacity = 0;

            Canvas.SetLeft(LeftStickDot, 118);
            Canvas.SetTop(LeftStickDot, 118);

            Canvas.SetLeft(RightStickDot, 118);
            Canvas.SetTop(RightStickDot, 118);
        }
    }
}
