using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadApp.Services
{
    public class VirtualGamepadService
    {
        private const int Ds4ReportExLength = 63;
        private const int Ds4TouchMaxX = 1919;
        private const int Ds4TouchMaxY = 942;
        private const byte Ds4TouchContactInactive = 0x80;
        private const byte Ds4BatteryFull = 0x0B;

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

        private bool ds4Touch1WasActive;
        private int ds4Touch1LastX;
        private int ds4Touch1LastY;
        private int ds4Touch1LastTrackingId;

        private bool ds4Touch2WasActive;
        private int ds4Touch2LastX;
        private int ds4Touch2LastY;
        private int ds4Touch2LastTrackingId;

        private byte ds4TouchPacketCounter;

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
            ResetDs4TouchState();

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

            byte[] report = BuildDs4ReportEx(state);

            ds4Controller.SubmitRawReport(report);
        }

        public byte[] BuildDs4ReportEx(GamepadOutputState state)
        {
            byte[] report = new byte[Ds4ReportExLength];

            report[0] = state.LeftStickX;
            report[1] = state.LeftStickY;
            report[2] = state.RightStickX;
            report[3] = state.RightStickY;

            ushort buttons = ComputeDS4Dpad(state.Buttons).Value;

            foreach (KeyValuePair<string, DualShock4Button> entry in DS4ButtonMap)
            {
                if (state.Buttons.Contains(entry.Key))
                    buttons |= entry.Value.Value;
            }

            // DS4 raporu tetiklerin analog değerlerini ve dijital basılı
            // bitlerini ayrı alanlarda taşır. Bazı oyun bağlamları yalnızca
            // bu bitleri okuduğundan ikisini aynı final state'ten üret.
            if (state.LeftTrigger > 0)
                buttons |= DualShock4Button.TriggerLeft.Value;
            if (state.RightTrigger > 0)
                buttons |= DualShock4Button.TriggerRight.Value;

            report[4] = (byte)(buttons & 0xFF);
            report[5] = (byte)(buttons >> 8);

            byte special = 0;
            if (state.PsPressed)
                special |= (byte)DualShock4SpecialButton.Ps.Value;
            if (state.TouchpadPressed)
                special |= (byte)DualShock4SpecialButton.Touchpad.Value;
            report[6] = special;

            report[7] = state.LeftTrigger;
            report[8] = state.RightTrigger;

            report[29] = Ds4BatteryFull;

            WriteDs4Touch(report, state);

            return report;
        }

        private void WriteDs4Touch(byte[] report, GamepadOutputState state)
        {
            bool touch1Active = state.Touch1Active;
            bool touch2Active = state.Touch2Active;

            bool sendPacket =
                touch1Active || touch2Active ||
                ds4Touch1WasActive || ds4Touch2WasActive;

            if (!sendPacket)
                return;

            report[32] = 1;
            report[33] = NextTouchPacketCounter();

            WriteDs4Touch1Slot(report, state, touch1Active);
            WriteDs4Touch2Slot(report, state, touch2Active);
        }

        private void WriteDs4Touch1Slot(
            byte[] report,
            GamepadOutputState state,
            bool active)
        {
            if (active)
            {
                int x = Math.Clamp(state.Touch1X, 0, Ds4TouchMaxX);
                int y = Math.Clamp(state.Touch1Y, 0, Ds4TouchMaxY);
                int id = state.Touch1TrackingId & 0x7F;

                if (!ds4Touch1WasActive)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Virtual DS4 Touch: Down X={x} Y={y} Id={id}");
                }

                WriteDs4TouchPoint(
                    report.AsSpan(34, 4),
                    active: true,
                    id,
                    x,
                    y);

                ds4Touch1WasActive = true;
                ds4Touch1LastX = x;
                ds4Touch1LastY = y;
                ds4Touch1LastTrackingId = id;
            }
            else if (ds4Touch1WasActive)
            {
                // Finger-up: DS4 protokolünün beklediği inactive contact
                // (active-low bit set) bir frame gönderilir.
                System.Diagnostics.Debug.WriteLine(
                    $"Virtual DS4 Touch: Up Id={ds4Touch1LastTrackingId}");

                WriteDs4TouchPoint(
                    report.AsSpan(34, 4),
                    active: false,
                    ds4Touch1LastTrackingId,
                    ds4Touch1LastX,
                    ds4Touch1LastY);

                ds4Touch1WasActive = false;
            }
            else
            {
                WriteDs4TouchPoint(
                    report.AsSpan(34, 4),
                    active: false,
                    trackingId: 0,
                    x: 0,
                    y: 0);
            }
        }

        private void WriteDs4Touch2Slot(
            byte[] report,
            GamepadOutputState state,
            bool active)
        {
            if (active)
            {
                int x = Math.Clamp(state.Touch2X, 0, Ds4TouchMaxX);
                int y = Math.Clamp(state.Touch2Y, 0, Ds4TouchMaxY);
                int id = state.Touch2TrackingId & 0x7F;

                if (!ds4Touch2WasActive)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Virtual DS4 Touch2: Down X={x} Y={y} Id={id}");
                }

                WriteDs4TouchPoint(
                    report.AsSpan(38, 4),
                    active: true,
                    id,
                    x,
                    y);

                ds4Touch2WasActive = true;
                ds4Touch2LastX = x;
                ds4Touch2LastY = y;
                ds4Touch2LastTrackingId = id;
            }
            else if (ds4Touch2WasActive)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Virtual DS4 Touch2: Up Id={ds4Touch2LastTrackingId}");

                WriteDs4TouchPoint(
                    report.AsSpan(38, 4),
                    active: false,
                    ds4Touch2LastTrackingId,
                    ds4Touch2LastX,
                    ds4Touch2LastY);

                ds4Touch2WasActive = false;
            }
            else
            {
                WriteDs4TouchPoint(
                    report.AsSpan(38, 4),
                    active: false,
                    trackingId: 0,
                    x: 0,
                    y: 0);
            }
        }

        private byte NextTouchPacketCounter()
        {
            return ds4TouchPacketCounter++;
        }

        private void ResetDs4TouchState()
        {
            ds4Touch1WasActive = false;
            ds4Touch1LastX = 0;
            ds4Touch1LastY = 0;
            ds4Touch1LastTrackingId = 0;

            ds4Touch2WasActive = false;
            ds4Touch2LastX = 0;
            ds4Touch2LastY = 0;
            ds4Touch2LastTrackingId = 0;
        }

        public static void WriteDs4TouchPoint(
            Span<byte> touchPoint,
            bool active,
            int trackingId,
            int x,
            int y)
        {
            int clampedX = Math.Clamp(x, 0, Ds4TouchMaxX);
            int clampedY = Math.Clamp(y, 0, Ds4TouchMaxY);
            int id = trackingId & 0x7F;

            touchPoint[0] = active
                ? (byte)id
                : (byte)(Ds4TouchContactInactive | id);
            touchPoint[1] = (byte)(clampedX & 0xFF);
            touchPoint[2] = (byte)(
                ((clampedX >> 8) & 0x0F) |
                ((clampedY & 0x0F) << 4));
            touchPoint[3] = (byte)((clampedY >> 4) & 0xFF);
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
