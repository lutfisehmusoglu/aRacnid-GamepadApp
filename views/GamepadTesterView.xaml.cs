using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using GamepadApp.Models;
using GamepadApp.Services;

namespace GamepadApp.Views
{
    public partial class GamepadTesterView : UserControl
    {
        private const int Ds4TouchMaxX = 1919;
        private const int Ds4TouchMaxY = 942;
        private const double MinTouchPointDistance = 2.5;

        private const double TrailMaxThickness = 18.0;
        private const double TrailMinThickness = 2.0;
        private const double TrailMaxOpacity = 1.0;
        private const double TrailMinOpacity = 0.0;
        private const long TrailSegmentLifetimeMs = 900;

        private const int MinimumSwipeDistance = 250;
        private const double MinimumSwipeDurationMs = 30;
        private const double MaximumSwipeDurationMs = 700;
        private const double SwipeDominanceRatio = 1.25;

        private readonly GamepadService gamepadService =
            new GamepadService();

        private readonly ButtonRemapService buttonRemapService =
            new ButtonRemapService();

        private readonly DispatcherTimer pollTimer;

        private bool isXboxMode;

        private readonly List<TouchTrailSegment> trailSegments = new();
        private Point lastTouchPoint;
        private int lastTouchTrackingId = -1;
        private bool lastTouchWasActive;
        private Color? trailColor;
        private TouchpadMode touchpadMode = TouchpadMode.Normal;

        private bool swipeGestureActive;
        private int swipeStartX;
        private int swipeStartY;
        private int swipeLastX;
        private int swipeLastY;
        private long swipeStartTicks;
        private int swipeTrackingId = -1;

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
                ResetTouchTrailState();
                ResetSwipeState();
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

            trailColor = mainWindow?.ActiveLightbarColor;

            TouchpadMode newMode =
                mainWindow?.EmulationService?.TouchpadMode ??
                TouchpadMode.Normal;

            if (newMode != touchpadMode)
            {
                touchpadMode = newMode;
                ResetTouchTrailState();
                ResetSwipeState();
            }

            var input = mainWindow?.EmulationService?.CurrentInput;

            if (input == null || !input.IsConnected)
            {
                ResetHighlights();
                ResetTouchTrailState();
                ResetSwipeState();
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
            UpdateTouchTrail(input);
            UpdateSwipeGesture(input);
        }

        private void UpdateAnalogSticks(
    GamepadOutputState output)
        {
            Canvas.SetLeft(
                LeftStickDot,
                (output.LeftStickX / 255.0) * 325 - 15);

            Canvas.SetTop(
                LeftStickDot,
                (output.LeftStickY / 255.0) * 325 - 15);

            Canvas.SetLeft(
                RightStickDot,
                (output.RightStickX / 255.0) * 325 - 15);

            Canvas.SetTop(
                RightStickDot,
                (output.RightStickY / 255.0) * 325 - 15);
        }

        private void UpdateTouchTrail(PhysicalGamepadState input)
        {
            if (isXboxMode || touchpadMode != TouchpadMode.Normal)
                return;

            if (input.Touch1Active)
                AddTrailPoint(input);

            UpdateTrailFade();
        }

        private void AddTrailPoint(PhysicalGamepadState input)
        {
            double width = TouchTrailCanvas.ActualWidth;
            double height = TouchTrailCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            double canvasX = Math.Clamp(
                input.Touch1X / (double)Ds4TouchMaxX * width,
                0,
                width);

            double canvasY = Math.Clamp(
                input.Touch1Y / (double)Ds4TouchMaxY * height,
                0,
                height);

            var point = new Point(canvasX, canvasY);

            bool isNewTouch =
                !lastTouchWasActive ||
                input.Touch1TrackingId != lastTouchTrackingId;

            if (isNewTouch)
            {
                // Yeni dokunuş: önceki noktayla birleştirme, yalnız başlangıç
                // noktasını kaydet. İlk gerçek segment bir sonraki hareket
                // noktasında oluşur.
                lastTouchPoint = point;
                lastTouchTrackingId = input.Touch1TrackingId;
                lastTouchWasActive = true;
                return;
            }

            if ((point - lastTouchPoint).Length <
                MinTouchPointDistance)
            {
                return;
            }

            var segment = new TouchTrailSegment
            {
                Shape = new Line
                {
                    X1 = lastTouchPoint.X,
                    Y1 = lastTouchPoint.Y,
                    X2 = point.X,
                    Y2 = point.Y,
                    Stroke = CreateTrailBrush(),
                    StrokeThickness = TrailMaxThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                },
                CreatedTicks = Stopwatch.GetTimestamp()
            };

            TouchTrailCanvas.Children.Add(segment.Shape);
            trailSegments.Add(segment);

            lastTouchPoint = point;
            lastTouchWasActive = true;
        }

        private Brush CreateTrailBrush()
        {
            if (trailColor is Color color)
                return new SolidColorBrush(color);

            return Brushes.White;
        }

        private void UpdateTrailFade()
        {
            if (trailSegments.Count == 0)
                return;

            long now = Stopwatch.GetTimestamp();

            for (int i = trailSegments.Count - 1; i >= 0; i--)
            {
                TouchTrailSegment segment = trailSegments[i];

                double ageMs =
                    (now - segment.CreatedTicks) *
                    1000.0 / Stopwatch.Frequency;

                if (ageMs >= TrailSegmentLifetimeMs)
                {
                    TouchTrailCanvas.Children.Remove(segment.Shape);
                    trailSegments.RemoveAt(i);
                    continue;
                }

                double ratio = ageMs / TrailSegmentLifetimeMs;

                // Ease-in: kuyruk bölümünde incelme hızlanır; baş
                // nispeten daha uzun süre dolgun kalır.
                double thicknessRatio = ratio * ratio;

                segment.Shape.StrokeThickness =
                    TrailMaxThickness +
                    (TrailMinThickness - TrailMaxThickness) * thicknessRatio;

                segment.Shape.Opacity =
                    TrailMaxOpacity +
                    (TrailMinOpacity - TrailMaxOpacity) * ratio;
            }
        }

        private void ResetTouchTrailState()
        {
            foreach (TouchTrailSegment segment in trailSegments)
                TouchTrailCanvas.Children.Remove(segment.Shape);

            trailSegments.Clear();

            lastTouchWasActive = false;
            lastTouchTrackingId = -1;
        }

        private void UpdateSwipeGesture(PhysicalGamepadState input)
        {
            if (isXboxMode)
                return;

            bool active = input.Touch1Active;

            if (!swipeGestureActive && active)
            {
                swipeGestureActive = true;
                swipeStartX = input.Touch1X;
                swipeStartY = input.Touch1Y;
                swipeLastX = input.Touch1X;
                swipeLastY = input.Touch1Y;
                swipeStartTicks = Stopwatch.GetTimestamp();
                swipeTrackingId = input.Touch1TrackingId;
                return;
            }

            if (!swipeGestureActive)
                return;

            if (!active)
            {
                EvaluateSwipe();
                ResetSwipeState();
                return;
            }

            if (input.Touch1TrackingId != swipeTrackingId)
            {
                // Aktif temas sırasında parmak değişti; eski gesture'ı
                // iptal edip yeni tracking ID ile yeni gesture başlat.
                swipeStartX = input.Touch1X;
                swipeStartY = input.Touch1Y;
                swipeLastX = input.Touch1X;
                swipeLastY = input.Touch1Y;
                swipeStartTicks = Stopwatch.GetTimestamp();
                swipeTrackingId = input.Touch1TrackingId;
                return;
            }

            swipeLastX = input.Touch1X;
            swipeLastY = input.Touch1Y;
        }

        private void EvaluateSwipe()
        {
            double elapsedMs =
                (Stopwatch.GetTimestamp() - swipeStartTicks) *
                1000.0 / Stopwatch.Frequency;

            if (elapsedMs < MinimumSwipeDurationMs ||
                elapsedMs > MaximumSwipeDurationMs)
            {
                return;
            }

            int deltaX = swipeLastX - swipeStartX;
            int deltaY = swipeLastY - swipeStartY;

            TouchSwipeDirection direction =
                ClassifySwipe(deltaX, deltaY);

            if (direction == TouchSwipeDirection.None)
                return;

            Debug.WriteLine(
                $"DS4 Swipe: {direction} " +
                $"dx={deltaX} dy={deltaY} duration={(int)elapsedMs}ms");
        }

        private TouchSwipeDirection ClassifySwipe(
            int deltaX,
            int deltaY)
        {
            int absX = Math.Abs(deltaX);
            int absY = Math.Abs(deltaY);

            if (absX >= absY * SwipeDominanceRatio &&
                absX >= MinimumSwipeDistance)
            {
                return deltaX > 0
                    ? TouchSwipeDirection.Right
                    : TouchSwipeDirection.Left;
            }

            if (absY >= absX * SwipeDominanceRatio &&
                absY >= MinimumSwipeDistance)
            {
                return deltaY > 0
                    ? TouchSwipeDirection.Down
                    : TouchSwipeDirection.Up;
            }

            return TouchSwipeDirection.None;
        }

        private void ResetSwipeState()
        {
            swipeGestureActive = false;
            swipeTrackingId = -1;
        }

        private enum TouchSwipeDirection
        {
            None,
            Left,
            Right,
            Up,
            Down
        }

        private sealed class TouchTrailSegment
        {
            public required Line Shape { get; init; }
            public long CreatedTicks { get; init; }
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

            Canvas.SetLeft(LeftStickDot, 147.5);
            Canvas.SetTop(LeftStickDot, 147.5);

            Canvas.SetLeft(RightStickDot, 147.5);
            Canvas.SetTop(RightStickDot, 147.5);
        }
    }
}
