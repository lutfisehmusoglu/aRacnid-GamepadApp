using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadApp.Services
{
    public class VirtualGamepadService
    {
        private readonly object sync = new();
        private ViGEmClient? client;
        private IXbox360Controller? xboxController;
        private IDualShock4Controller? ds4Controller;
        private Thread? ds4FeedbackThread;
        private volatile bool ds4FeedbackRunning;
        private VirtualControllerType currentMode =
            VirtualControllerType.DualShock4;
        private bool isDisconnected;
        private bool disposed;

        public event Action<byte, byte>? FeedbackReceived;

        public bool IsConnected
        {
            get
            {
                lock (sync)
                    return !disposed && !isDisconnected;
            }
        }

        private static readonly Dictionary<string, Xbox360Button> XboxButtonMap =
            new()
            {
                ["Cross"] = Xbox360Button.A,
                ["Circle"] = Xbox360Button.B,
                ["Square"] = Xbox360Button.X,
                ["Triangle"] = Xbox360Button.Y,
                ["D-Pad Up"] = Xbox360Button.Up,
                ["D-Pad Down"] = Xbox360Button.Down,
                ["D-Pad Left"] = Xbox360Button.Left,
                ["D-Pad Right"] = Xbox360Button.Right,
                ["L1"] = Xbox360Button.LeftShoulder,
                ["R1"] = Xbox360Button.RightShoulder,
                ["L3"] = Xbox360Button.LeftThumb,
                ["R3"] = Xbox360Button.RightThumb,
                ["Share"] = Xbox360Button.Back,
                ["Options"] = Xbox360Button.Start,
            };

        private static readonly Dictionary<string, DualShock4Button> DS4ButtonMap =
            new()
            {
                ["Cross"] = DualShock4Button.Cross,
                ["Circle"] = DualShock4Button.Circle,
                ["Square"] = DualShock4Button.Square,
                ["Triangle"] = DualShock4Button.Triangle,
                ["L1"] = DualShock4Button.ShoulderLeft,
                ["R1"] = DualShock4Button.ShoulderRight,
                ["L3"] = DualShock4Button.ThumbLeft,
                ["R3"] = DualShock4Button.ThumbRight,
                ["Share"] = DualShock4Button.Share,
                ["Options"] = DualShock4Button.Options,
            };

        public VirtualGamepadService()
        {
            isDisconnected = true;

            try
            {
                client = new ViGEmClient();
            }
            catch (Exception ex)
            {
                client = null;

                System.Diagnostics.Debug.WriteLine(
                    $"ViGEmBus kullanılamıyor: {ex}");
            }
        }

        public void SwitchMode(VirtualControllerType type)
        {
            lock (sync)
            {
                if (disposed || client == null)
                    return;

                if (type == currentMode && !isDisconnected)
                    return;

                DisconnectCurrent();
                ConnectAs(type);
            }
        }

        private void DisconnectCurrent(bool resetState = true)
        {
            if (resetState)
            {
                try
                {
                    ResetStateInternal();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Sanal kol nötrleme hatası: {ex.Message}");
                }
            }

            IXbox360Controller? xboxToClose = xboxController;
            IDualShock4Controller? ds4ToClose = ds4Controller;
            xboxController = null;
            ds4Controller = null;

            if (xboxToClose != null)
                xboxToClose.FeedbackReceived -= XboxController_FeedbackReceived;

            try
            {
                xboxToClose?.Disconnect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Sanal Xbox ayırma hatası: {ex.Message}");
            }

            try
            {
                if (ds4ToClose != null)
                    StopDs4FeedbackPump(ds4ToClose);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Sanal DS4 ayırma hatası: {ex.Message}");
            }

            try { (xboxToClose as IDisposable)?.Dispose(); } catch { }
            // Pump zamanında durmadıysa native handle hâlâ kullanımda olabilir;
            // bu durumda closure controller'ı canlı tutar ve GC daha sonra temizler.
            if (ds4ToClose == null || ds4FeedbackThread == null)
            {
                try { ds4ToClose?.Dispose(); } catch { }
            }
            isDisconnected = true;
            PublishFeedback(0, 0);
        }

        private void ConnectAs(VirtualControllerType type)
        {
            ViGEmClient? activeClient = client;

            if (activeClient == null)
                return;

            currentMode = type;

            try
            {
                if (type == VirtualControllerType.Xbox360)
                {
                    xboxController = activeClient.CreateXbox360Controller();

                    xboxController.AutoSubmitReport = false;
                    xboxController.FeedbackReceived +=
                        XboxController_FeedbackReceived;

                    xboxController.Connect();
                }
                else
                {
                    ds4Controller = activeClient.CreateDualShock4Controller();

                    ds4Controller.AutoSubmitReport = false;

                    ds4Controller.Connect();
                    StartDs4FeedbackPump(ds4Controller);
                }

                isDisconnected = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Sanal kol bağlama hatası: {ex.Message}");

                if (xboxController != null)
                {
                    xboxController.FeedbackReceived -=
                        XboxController_FeedbackReceived;
                    try { xboxController.Disconnect(); } catch { }
                    try { (xboxController as IDisposable)?.Dispose(); } catch { }
                }

                if (ds4Controller != null)
                {
                    bool stopped = false;
                    try { stopped = StopDs4FeedbackPump(ds4Controller); } catch { }
                    if (stopped)
                    {
                        try { ds4Controller.Dispose(); } catch { }
                    }
                }

                xboxController = null;
                ds4Controller = null;
                isDisconnected = true;
            }
        }

        public void ApplyState(GamepadOutputState state)
        {
            lock (sync)
            {
                if (isDisconnected) return;

                try
                {
                    if (currentMode == VirtualControllerType.Xbox360)
                    {
                        ApplyXboxState(state);
                    }
                    else
                    {
                        ApplyDS4State(state);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Sanal kol report hatası: {ex.Message}");

                    // Hedef/sürücü kaybolduysa çağıran thread'e exception
                    // taşıma. Bir sonraki fiziksel frame yeniden bağlamayı dener.
                    DisconnectCurrent(resetState: false);
                }
            }
        }

        private void ApplyXboxState(GamepadOutputState state)
        {
            if (xboxController == null) return;

            foreach (var btnEntry in XboxButtonMap)
            {
                bool pressed = state.Buttons.Contains(btnEntry.Key);
                xboxController.SetButtonState(btnEntry.Value, pressed);
            }

            xboxController.SetButtonState(
                Xbox360Button.Guide,
                state.PsPressed);

            xboxController.SetSliderValue(
                Xbox360Slider.LeftTrigger, state.LeftTrigger);
            xboxController.SetSliderValue(
                Xbox360Slider.RightTrigger, state.RightTrigger);

            xboxController.SetAxisValue(Xbox360Axis.LeftThumbX,
                ToSignedAxis(state.LeftStickX));
            xboxController.SetAxisValue(Xbox360Axis.LeftThumbY,
                ToSignedAxisInverted(state.LeftStickY));

            xboxController.SetAxisValue(Xbox360Axis.RightThumbX,
                ToSignedAxis(state.RightStickX));
            xboxController.SetAxisValue(Xbox360Axis.RightThumbY,
                ToSignedAxisInverted(state.RightStickY));
            xboxController.SubmitReport();
        }

        private static short ToSignedAxis(byte value)
        {
            int v = (value - 128) * 256;
            return (short)Math.Clamp(v, -32768, 32767);
        }

        private static short ToSignedAxisInverted(byte value)
        {
            int v = (128 - value) * 256;
            return (short)Math.Clamp(v, -32768, 32767);
        }

        private void ApplyDS4State(GamepadOutputState state)
        {
            if (ds4Controller == null) return;

            ds4Controller.ResetReport();

            foreach (var btnEntry in DS4ButtonMap)
            {
                bool pressed = state.Buttons.Contains(btnEntry.Key);
                ds4Controller.SetButtonState(btnEntry.Value, pressed);
            }

            ds4Controller.SetButtonState(
                DualShock4SpecialButton.Ps,
                state.PsPressed);
            ds4Controller.SetButtonState(
                DualShock4SpecialButton.Touchpad,
                state.TouchpadPressed);

            var dPad = ComputeDS4Dpad(state.Buttons);
            ds4Controller.SetDPadDirection(dPad);

            ds4Controller.SetSliderValue(
                DualShock4Slider.LeftTrigger, state.LeftTrigger);
            ds4Controller.SetSliderValue(
                DualShock4Slider.RightTrigger, state.RightTrigger);

            // DS4 raporu tetiklerin analog değerlerini ve dijital basılı
            // bitlerini ayrı alanlarda taşır. Bazı oyun bağlamları yalnızca
            // bu bitleri okuduğundan ikisini aynı final state'ten üret.
            ds4Controller.SetButtonState(
                DualShock4Button.TriggerLeft,
                state.LeftTrigger > 0);
            ds4Controller.SetButtonState(
                DualShock4Button.TriggerRight,
                state.RightTrigger > 0);

            ds4Controller.SetAxisValue(
                DualShock4Axis.LeftThumbX, state.LeftStickX);
            ds4Controller.SetAxisValue(
                DualShock4Axis.LeftThumbY, state.LeftStickY);

            ds4Controller.SetAxisValue(
                DualShock4Axis.RightThumbX, state.RightStickX);
            ds4Controller.SetAxisValue(
                DualShock4Axis.RightThumbY, state.RightStickY);

            ds4Controller.SubmitReport();
        }

        private static DualShock4DPadDirection ComputeDS4Dpad(
            HashSet<string> buttons)
        {
            bool up = buttons.Contains("D-Pad Up");
            bool down = buttons.Contains("D-Pad Down");
            bool left = buttons.Contains("D-Pad Left");
            bool right = buttons.Contains("D-Pad Right");

            return (up, down, left, right) switch
            {
                (true, false, false, true) => DualShock4DPadDirection.Northeast,
                (true, false, true, false) => DualShock4DPadDirection.Northwest,
                (false, true, false, true) => DualShock4DPadDirection.Southeast,
                (false, true, true, false) => DualShock4DPadDirection.Southwest,
                (true, false, false, false) => DualShock4DPadDirection.North,
                (false, true, false, false) => DualShock4DPadDirection.South,
                (false, false, true, false) => DualShock4DPadDirection.West,
                (false, false, false, true) => DualShock4DPadDirection.East,
                _ => DualShock4DPadDirection.None,
            };
        }

        private void ResetStateInternal()
        {
            var neutral = new GamepadOutputState();
            ApplyState(neutral);
        }

        public void ResetState()
        {
            lock (sync)
            {
                if (isDisconnected) return;
                ResetStateInternal();
            }
        }

        public void DisconnectVirtualController()
        {
            lock (sync)
            {
                if (isDisconnected)
                    return;

                DisconnectCurrent();
            }
        }

        public void Disconnect()
        {
            lock (sync)
            {
                if (disposed)
                    return;

                if (!isDisconnected)
                    DisconnectCurrent();

                try
                {
                    client?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ViGEm istemcisi kapatma hatası: {ex.Message}");
                }
                client = null;
                disposed = true;
            }
        }

        private void XboxController_FeedbackReceived(
            object sender,
            Xbox360FeedbackReceivedEventArgs e)
        {
            if (!ReferenceEquals(sender, xboxController))
                return;

            PublishFeedback(e.LargeMotor, e.SmallMotor);
        }

        private void StartDs4FeedbackPump(IDualShock4Controller controller)
        {
            ds4FeedbackRunning = true;
            ds4FeedbackThread = new Thread(() => Ds4FeedbackLoop(controller))
            {
                IsBackground = true,
                Name = "aRacnid DS4 Feedback"
            };
            ds4FeedbackThread.Start();
        }

        private bool StopDs4FeedbackPump(IDualShock4Controller controller)
        {
            ds4FeedbackRunning = false;

            // Native await çağrısını hemen çözmek için önce hedefi ayır. Pump
            // tamamen çıkmadan controller dispose edilmez.
            try { controller.Disconnect(); } catch { }

            Thread? thread = ds4FeedbackThread;
            bool stopped = thread == null ||
                           ReferenceEquals(thread, Thread.CurrentThread);

            if (!stopped)
            {
                try { stopped = thread!.Join(1000); } catch { }
            }

            if (!stopped)
            {
                System.Diagnostics.Debug.WriteLine(
                    "DS4 feedback thread zamanında kapanmadı; dispose ertelendi.");
            }

            if (stopped)
                ds4FeedbackThread = null;

            return stopped;
        }

        private void Ds4FeedbackLoop(IDualShock4Controller controller)
        {
            int consecutiveFailures = 0;

            while (ds4FeedbackRunning &&
                   ReferenceEquals(controller, ds4Controller))
            {
                try
                {
                    IEnumerable<byte> raw = controller.AwaitRawOutputReport(
                        150,
                        out bool timedOut);

                    if (timedOut)
                        continue;

                    consecutiveFailures = 0;
                    byte[] report = raw as byte[] ?? raw.Take(64).ToArray();

                    // 0x05 DS4 output raporunda bit0 rumble-valid'dir.
                    // Yalnız lightbar raporu geldiğinde sıfır motor baytlarını
                    // gerçek bir "titreşimi durdur" komutu olarak yorumlama.
                    if (TryParseDs4RumbleFeedback(
                            report,
                            out byte largeMotor,
                            out byte smallMotor))
                    {
                        PublishFeedback(
                            largeMotor,
                            smallMotor);
                    }
                }
                catch (Exception ex)
                {
                    if (ds4FeedbackRunning)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"DS4 feedback okuma hatası: {ex.Message}");
                    }

                    if (!ds4FeedbackRunning || ++consecutiveFailures >= 3)
                        break;

                    Thread.Sleep(250);
                }
            }

        }

        public static bool TryParseDs4RumbleFeedback(
            ReadOnlySpan<byte> report,
            out byte largeMotor,
            out byte smallMotor)
        {
            largeMotor = 0;
            smallMotor = 0;

            if (report.Length <= 5 ||
                report[0] != 0x05 ||
                (report[1] & 0x01) == 0)
            {
                return false;
            }

            smallMotor = report[4];
            largeMotor = report[5];
            return true;
        }

        private void PublishFeedback(byte largeMotor, byte smallMotor)
        {
            try
            {
                FeedbackReceived?.Invoke(largeMotor, smallMotor);
            }
            catch (Exception ex)
            {
                // ViGEm native callback thread'inden exception çıkmasına izin
                // verme. Dinleyici yalnız fiziksel output kuyruğunu günceller.
                System.Diagnostics.Debug.WriteLine(
                    $"Titreşim geri bildirimi işlenemedi: {ex.Message}");
            }
        }
    }
}
