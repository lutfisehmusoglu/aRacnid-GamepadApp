using System.Windows.Threading;
using GamepadApp.Models;

namespace GamepadApp.Services;

public class GamepadEmulationService
{
    private readonly GamepadService gamepadService;
    private readonly VirtualGamepadService virtualGamepadService;
    private readonly ButtonRemapService buttonRemapService;
    private readonly Profile activeProfile;
    private readonly PhysicalGamepadManager physicalGamepadManager = new();
    private readonly MouseInputService mouseInputService = new();
    private readonly DispatcherTimer timer;
    private readonly object feedbackSync = new();

    private Thread? readThread;
    private volatile bool running;
    private volatile bool sourceConnected;
    private volatile PhysicalGamepadState? latestState;
    private bool acceptFeedback;
    private bool feedbackSubscribed;

    public PhysicalGamepadState CurrentInput { get; } = new();

    public PhysicalGamepadDescriptor? CurrentDescriptor =>
        physicalGamepadManager.CurrentDescriptor;

    public TouchpadMode TouchpadMode =>
        activeProfile.ControllerSettings?.TouchpadMode ??
        TouchpadMode.Normal;

    public GamepadEmulationService(
        GamepadService gamepadService,
        VirtualGamepadService virtualGamepadService,
        ButtonRemapService buttonRemapService,
        Profile activeProfile)
    {
        this.gamepadService = gamepadService;
        this.virtualGamepadService = virtualGamepadService;
        this.buttonRemapService = buttonRemapService;
        this.activeProfile = activeProfile;

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (running)
            return;

        running = true;

        lock (feedbackSync)
        {
            acceptFeedback = true;
            if (!feedbackSubscribed)
            {
                virtualGamepadService.FeedbackReceived +=
                    VirtualGamepadService_FeedbackReceived;
                feedbackSubscribed = true;
            }
        }

        readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "aRacnid Physical Input"
        };

        readThread.Start();
        timer.Start();
    }

    public void Stop()
    {
        lock (feedbackSync)
        {
            acceptFeedback = false;
            if (feedbackSubscribed)
            {
                virtualGamepadService.FeedbackReceived -=
                    VirtualGamepadService_FeedbackReceived;
                feedbackSubscribed = false;
            }
        }

        // Önce ViGEm hedefini kapat, ardından fiziksel sıfır-rumble komutunu
        // read thread hâlâ çalışırken kuyruğa koy. DS4/SDL outputları yalnız
        // ReadNext içindeki flush yolundan native cihaza gönderilir.
        virtualGamepadService.ResetState();
        virtualGamepadService.Disconnect();
        mouseInputService.Reset();
        physicalGamepadManager.TrySetVibration(0, 0, 0);
        physicalGamepadManager.WaitForPendingOutput(1200);

        running = false;
        timer.Stop();

        try
        {
            readThread?.Join(1200);
        }
        catch
        {
        }

        physicalGamepadManager.Dispose();
        latestState = null;
        sourceConnected = false;
        CurrentInput.Reset();
    }

    public bool TrySetVibration(
        byte leftMotor,
        byte rightMotor,
        uint durationMs = 500)
    {
        return physicalGamepadManager.TrySetVibration(
            leftMotor,
            rightMotor,
            durationMs);
    }

    public bool TrySetLightbar(byte red, byte green, byte blue)
    {
        return physicalGamepadManager.TrySetLightbar(red, green, blue);
    }

    private void ReadLoop()
    {
        while (running)
        {
            if (!physicalGamepadManager.IsConnected &&
                !physicalGamepadManager.TryConnect())
            {
                Thread.Sleep(300);
                continue;
            }

            PhysicalReadResult result =
                physicalGamepadManager.ReadNext(out PhysicalGamepadState? state);

            switch (result)
            {
                case PhysicalReadResult.State when state != null:
                    latestState = state;
                    sourceConnected = true;
                    SubmitOutputFrame(state);
                    break;
                case PhysicalReadResult.Disconnected:
                    latestState = null;
                    sourceConnected = false;
                    mouseInputService.Reset();
                    virtualGamepadService.DisconnectVirtualController();
                    physicalGamepadManager.TrySetVibration(0, 0, 0);
                    break;
            }
        }
    }

    private void Poll()
    {
        PhysicalGamepadState? snapshot = latestState;

        if (!sourceConnected || snapshot == null)
        {
            if (CurrentInput.IsConnected)
                CurrentInput.Reset();
            return;
        }

        CurrentInput.CopyFrom(snapshot);
    }

    private void SubmitOutputFrame(PhysicalGamepadState input)
    {
        // Sanal controller yalnız ilk geçerli fiziksel frame geldikten sonra
        // oluşturulur. Fiziksel read cadence'i korunur; WPF/UI gecikmeleri kısa
        // tuş darbelerini veya disconnect nötrlemesini artık yutamaz.
        virtualGamepadService.SwitchMode(
            GamepadService.SelectedVirtualType);

        TouchpadMode mode = TouchpadMode;

        GamepadOutputState output = BuildOutputState(input, mode);
        virtualGamepadService.ApplyState(output);

        if (mode == TouchpadMode.Mouse)
            mouseInputService.ProcessTouch(input);
        else
            mouseInputService.Reset();
    }

    public GamepadOutputState BuildOutputState(
        PhysicalGamepadState input)
    {
        return BuildOutputState(input, TouchpadMode);
    }

    private GamepadOutputState BuildOutputState(
        PhysicalGamepadState input,
        TouchpadMode touchpadMode)
    {
        var output = new GamepadOutputState();

        ProcessSticks(input, output);
        ProcessButtons(input, output);
        ProcessTriggers(input, output);
        output.PsPressed = input.PsPressed;

        // Touchpad click yalnız Normal modda sanal DS4 touchpad button'ına
        // taşınır. Mouse modunda click sol fare tıkına dönüşür; Kapalı modda
        // tamamen yok sayılır.
        output.TouchpadPressed =
            touchpadMode == TouchpadMode.Normal &&
            input.TouchpadPressed;

        if (touchpadMode == TouchpadMode.Normal)
        {
            output.Touch1Active = input.Touch1Active;
            output.Touch1X = input.Touch1X;
            output.Touch1Y = input.Touch1Y;
            output.Touch1TrackingId = input.Touch1TrackingId;
            output.Touch2Active = input.Touch2Active;
            output.Touch2X = input.Touch2X;
            output.Touch2Y = input.Touch2Y;
            output.Touch2TrackingId = input.Touch2TrackingId;
        }

        return output;
    }

    private void ProcessSticks(
        PhysicalGamepadState input,
        GamepadOutputState output)
    {
        var leftStick = gamepadService.ApplyDeadzone(
            input.LeftStickX,
            input.LeftStickY,
            GamepadService.DeadzonePercent);

        leftStick = gamepadService.ApplyAntiDeadzone(
            leftStick.X,
            leftStick.Y,
            GamepadService.AntiDeadzonePercent);

        leftStick = gamepadService.ApplySensitivity(
            leftStick.X,
            leftStick.Y,
            GamepadService.SensitivityPercent);

        var rightStick = gamepadService.ApplyDeadzone(
            input.RightStickX,
            input.RightStickY,
            GamepadService.DeadzonePercent);

        rightStick = gamepadService.ApplyAntiDeadzone(
            rightStick.X,
            rightStick.Y,
            GamepadService.AntiDeadzonePercent);

        rightStick = gamepadService.ApplySensitivity(
            rightStick.X,
            rightStick.Y,
            GamepadService.SensitivityPercent);

        ControllerProfileSettings settings =
            activeProfile.ControllerSettings;

        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "LS X-",
            Math.Max(0, 128 - leftStick.X),
            128,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "LS X+",
            Math.Max(0, leftStick.X - 128),
            127,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "LS Y+",
            Math.Max(0, 128 - leftStick.Y),
            128,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "LS Y-",
            Math.Max(0, leftStick.Y - 128),
            127,
            output);

        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "RS X-",
            Math.Max(0, 128 - rightStick.X),
            128,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "RS X+",
            Math.Max(0, rightStick.X - 128),
            127,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "RS Y+",
            Math.Max(0, 128 - rightStick.Y),
            128,
            output);
        buttonRemapService.ApplyMappedStickDirection(
            settings,
            "RS Y-",
            Math.Max(0, rightStick.Y - 128),
            127,
            output);
    }

    private void ProcessButtons(
        PhysicalGamepadState input,
        GamepadOutputState output)
    {
        ControllerProfileSettings settings =
            activeProfile.ControllerSettings;

        foreach (string physical in input.Buttons)
        {
            buttonRemapService.ApplyMappedInput(
                settings,
                physical,
                byte.MaxValue,
                output);
        }
    }

    private void ProcessTriggers(
        PhysicalGamepadState input,
        GamepadOutputState output)
    {
        ControllerProfileSettings settings =
            activeProfile.ControllerSettings;

        buttonRemapService.ApplyMappedInput(
            settings,
            "L2",
            input.LeftTrigger,
            output,
            activationThreshold: 30);

        buttonRemapService.ApplyMappedInput(
            settings,
            "R2",
            input.RightTrigger,
            output,
            activationThreshold: 30);
    }

    private void VirtualGamepadService_FeedbackReceived(
        byte largeMotor,
        byte smallMotor)
    {
        lock (feedbackSync)
        {
            if (!acceptFeedback)
                return;

            ControllerProfileSettings settings =
                activeProfile.ControllerSettings;

            byte leftMotor = ScaleMotorStrength(
                largeMotor,
                settings.LeftMotorStrength);
            byte rightMotor = ScaleMotorStrength(
                smallMotor,
                settings.RightMotorStrength);

            physicalGamepadManager.TrySetVibration(
                leftMotor,
                rightMotor,
                durationMs: uint.MaxValue);
        }
    }

    public static byte ScaleMotorStrength(byte value, double percent)
    {
        double normalized = Math.Clamp(percent, 0, 100) / 100.0;
        return (byte)Math.Clamp(
            (int)Math.Round(value * normalized),
            byte.MinValue,
            byte.MaxValue);
    }
}
