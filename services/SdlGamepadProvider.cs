using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamepadApp.Services;

public static class SdlGamepadValueConverter
{
    public static byte StickToByte(short value)
    {
        int unsigned = value + 32768;
        return (byte)((unsigned * 255L + 32767) / 65535);
    }

    public static byte TriggerToByte(short value)
    {
        int positive = Math.Max(0, (int)value);
        return (byte)((positive * 255L + 16383) / 32767);
    }
}

public sealed record SdlSupportedGamepad(
    string DisplayName,
    PhysicalControllerType ControllerType,
    PhysicalConnectionType DefaultConnectionType,
    bool SupportsRumble,
    bool SupportsLightbar);

public static class SdlGamepadClassifier
{
    public static bool TryClassify(
        ushort vendorId,
        ushort productId,
        int sdlGamepadType,
        out SdlSupportedGamepad supported)
    {
        supported = null!;

        if (vendorId == 0x054C)
        {
            if (productId == 0x0CE6)
            {
                supported = new SdlSupportedGamepad(
                    "DualSense",
                    PhysicalControllerType.DualSense,
                    PhysicalConnectionType.Unknown,
                    SupportsRumble: true,
                    SupportsLightbar: true);
                return true;
            }

            if (productId == 0x0DF2)
            {
                supported = new SdlSupportedGamepad(
                    "DualSense Edge",
                    PhysicalControllerType.DualSenseEdge,
                    PhysicalConnectionType.Unknown,
                    SupportsRumble: true,
                    SupportsLightbar: true);
                return true;
            }

            return false;
        }

        if (vendorId == 0x057E)
        {
            if (productId == 0x2009)
            {
                supported = new SdlSupportedGamepad(
                    "Nintendo Switch Pro Controller",
                    PhysicalControllerType.NintendoSwitchPro,
                    PhysicalConnectionType.Unknown,
                    SupportsRumble: true,
                    SupportsLightbar: false);
                return true;
            }

            if (productId is 0x2006 or 0x2007 or 0x2008 or 0x200E)
            {
                string name = productId switch
                {
                    0x2006 => "Nintendo Joy-Con (L)",
                    0x2007 => "Nintendo Joy-Con (R)",
                    0x200E => "Nintendo Joy-Con Grip",
                    _ => "Nintendo Joy-Con Pair"
                };

                supported = new SdlSupportedGamepad(
                    name,
                    PhysicalControllerType.NintendoJoyCon,
                    PhysicalConnectionType.Unknown,
                    SupportsRumble: true,
                    SupportsLightbar: false);
                return true;
            }

            return false;
        }

        if (vendorId != 0x046D)
            return false;

        switch (productId)
        {
            case 0xC216:
            case 0xC21D:
                supported = Logitech(
                    "Logitech F310",
                    PhysicalControllerType.LogitechF310,
                    rumble: false);
                return true;
            case 0xC218:
            case 0xC21E:
                supported = Logitech(
                    "Logitech F510",
                    PhysicalControllerType.LogitechF510,
                    rumble: true);
                return true;
            case 0xC219:
            case 0xC21F:
                supported = new SdlSupportedGamepad(
                    "Logitech F710",
                    PhysicalControllerType.LogitechF710,
                    PhysicalConnectionType.WirelessReceiver,
                    SupportsRumble: true,
                    SupportsLightbar: false);
                return true;
            default:
                return false;
        }
    }

    private static SdlSupportedGamepad Logitech(
        string displayName,
        PhysicalControllerType type,
        bool rumble)
    {
        return new SdlSupportedGamepad(
            displayName,
            type,
            PhysicalConnectionType.USB,
            rumble,
            SupportsLightbar: false);
    }
}

internal sealed class SdlGamepadProvider : IPhysicalGamepadProvider
{
    private readonly bool runtimeAcquired;

    public SdlGamepadProvider()
    {
        runtimeAcquired = SdlGamepadRuntime.TryAcquire();
    }

    public IPhysicalGamepadSession? TryOpen()
    {
        if (!runtimeAcquired)
            return null;

        nint gamepadIds = nint.Zero;

        try
        {
            Sdl3Native.SDL_UpdateGamepads();
            gamepadIds = Sdl3Native.SDL_GetGamepads(out int count);

            if (gamepadIds == nint.Zero || count <= 0)
                return null;

            for (int index = 0; index < count; index++)
            {
                uint instanceId = unchecked(
                    (uint)Marshal.ReadInt32(gamepadIds, index * sizeof(int)));

                ushort vendorId =
                    Sdl3Native.SDL_GetGamepadVendorForID(instanceId);
                ushort productId =
                    Sdl3Native.SDL_GetGamepadProductForID(instanceId);
                int gamepadType =
                    Sdl3Native.SDL_GetGamepadTypeForID(instanceId);

                if (!SdlGamepadClassifier.TryClassify(
                        vendorId,
                        productId,
                        gamepadType,
                        out SdlSupportedGamepad supported))
                {
                    continue;
                }

                nint gamepad = Sdl3Native.SDL_OpenGamepad(instanceId);

                if (gamepad == nint.Zero)
                {
                    Debug.WriteLine(
                        $"SDL gamepad açılamadı: {Sdl3Native.GetError()}");
                    continue;
                }

                try
                {
                    string path = Sdl3Native.GetUtf8String(
                        Sdl3Native.SDL_GetGamepadPathForID(instanceId));

                    return new SdlGamepadSession(
                        gamepad,
                        instanceId,
                        path,
                        vendorId,
                        productId,
                        supported);
                }
                catch
                {
                    Sdl3Native.SDL_CloseGamepad(gamepad);
                    throw;
                }
            }
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or
            BadImageFormatException or
            EntryPointNotFoundException)
        {
            Debug.WriteLine($"SDL3 kullanılamıyor: {ex.Message}");
        }
        finally
        {
            if (gamepadIds != nint.Zero)
                Sdl3Native.SDL_free(gamepadIds);
        }

        return null;
    }

    public void Dispose()
    {
        if (runtimeAcquired)
            SdlGamepadRuntime.Release();
    }

    private sealed class SdlGamepadSession : IPhysicalGamepadSession
    {
        private const int MaxOutputAttempts = 3;
        private const int OutputRetryDelayMs = 25;
        private const int AxisLeftX = 0;
        private const int AxisLeftY = 1;
        private const int AxisRightX = 2;
        private const int AxisRightY = 3;
        private const int AxisLeftTrigger = 4;
        private const int AxisRightTrigger = 5;

        private readonly nint gamepad;
        private readonly object outputSync = new();
        private readonly ManualResetEventSlim outputFlushed = new(true);
        private SdlPendingOutputState outputState;
        private long pendingOutputVersion;
        private long flushedOutputVersion;
        private long pendingRumbleVersion;
        private long flushedRumbleVersion;
        private long pendingLightbarVersion;
        private long flushedLightbarVersion;
        private long rumbleRetryAfterTicks;
        private long lightbarRetryAfterTicks;
        private int rumbleAttemptCount;
        private int lightbarAttemptCount;
        private bool rumbleDeliveryFailed;
        private bool lightbarDeliveryFailed;
        private long sequenceNumber;
        private int? lastBatteryPercentage;
        private int batteryPollCountdown;
        private volatile bool disposed;

        public SdlGamepadSession(
            nint gamepad,
            uint instanceId,
            string path,
            ushort vendorId,
            ushort productId,
            SdlSupportedGamepad supported)
        {
            this.gamepad = gamepad;

            PhysicalConnectionType connection =
                supported.DefaultConnectionType == PhysicalConnectionType.Unknown
                    ? ToConnectionType(
                        Sdl3Native.SDL_GetGamepadConnectionState(gamepad))
                    : supported.DefaultConnectionType;

            string deviceId = string.IsNullOrWhiteSpace(path)
                ? $"SDL:{instanceId}:{vendorId:X4}:{productId:X4}"
                : path;

            Descriptor = new PhysicalGamepadDescriptor(
                deviceId,
                supported.DisplayName,
                supported.ControllerType,
                connection,
                vendorId,
                productId,
                supported.SupportsRumble,
                supported.SupportsLightbar);
        }

        public PhysicalGamepadDescriptor Descriptor { get; }

        public PhysicalReadResult ReadNext(out PhysicalGamepadState? state)
        {
            state = null;

            if (disposed)
                return PhysicalReadResult.Disconnected;

            try
            {
                Thread.Sleep(4);
                Sdl3Native.SDL_UpdateGamepads();

                if (!Sdl3Native.SDL_GamepadConnected(gamepad))
                    return PhysicalReadResult.Disconnected;

                FlushOutputCommands();

                if (--batteryPollCountdown <= 0)
                {
                    UpdateBattery();
                    batteryPollCountdown = 250;
                }

                var snapshot = new PhysicalGamepadState
                {
                    IsConnected = true,
                    SequenceNumber = Interlocked.Increment(ref sequenceNumber),
                    TimestampTicks = Stopwatch.GetTimestamp(),
                    LeftStickX = ReadStick(AxisLeftX),
                    LeftStickY = ReadStick(AxisLeftY),
                    RightStickX = ReadStick(AxisRightX),
                    RightStickY = ReadStick(AxisRightY),
                    LeftTrigger = ReadTrigger(AxisLeftTrigger),
                    RightTrigger = ReadTrigger(AxisRightTrigger),
                    PsPressed = ReadButton(SdlGamepadButton.Guide),
                    TouchpadPressed = ReadButton(SdlGamepadButton.Touchpad),
                    BatteryPercentage = lastBatteryPercentage
                };

                AddButton(snapshot, SdlGamepadButton.South, "Cross");
                AddButton(snapshot, SdlGamepadButton.East, "Circle");
                AddButton(snapshot, SdlGamepadButton.West, "Square");
                AddButton(snapshot, SdlGamepadButton.North, "Triangle");
                AddButton(snapshot, SdlGamepadButton.LeftShoulder, "L1");
                AddButton(snapshot, SdlGamepadButton.RightShoulder, "R1");
                AddButton(snapshot, SdlGamepadButton.LeftStick, "L3");
                AddButton(snapshot, SdlGamepadButton.RightStick, "R3");
                AddButton(snapshot, SdlGamepadButton.Back, "Share");
                AddButton(snapshot, SdlGamepadButton.Start, "Options");
                AddButton(snapshot, SdlGamepadButton.DpadUp, "D-Pad Up");
                AddButton(snapshot, SdlGamepadButton.DpadDown, "D-Pad Down");
                AddButton(snapshot, SdlGamepadButton.DpadLeft, "D-Pad Left");
                AddButton(snapshot, SdlGamepadButton.DpadRight, "D-Pad Right");

                state = snapshot;
                return PhysicalReadResult.State;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SDL gamepad okuma hatası: {ex.Message}");
                return PhysicalReadResult.Disconnected;
            }
        }

        public bool TrySetVibration(
            byte leftMotor,
            byte rightMotor,
            uint durationMs)
        {
            if (disposed || !Descriptor.SupportsRumble)
                return false;

            lock (outputSync)
            {
                if (disposed)
                    return false;

                outputState = outputState with
                {
                    LeftMotor = leftMotor,
                    RightMotor = rightMotor,
                    // SDL'de 0 süre "hemen durdur" demektir. Ortak fiziksel
                    // çıktı sözleşmesinde ise non-zero motorlarla 0, bir sonraki
                    // feedback gelene kadar sürekli titreşim anlamına gelir.
                    DurationMs = durationMs == 0 &&
                                 (leftMotor != 0 || rightMotor != 0)
                        ? uint.MaxValue
                        : durationMs,
                    HasRumble = true
                };
                pendingOutputVersion++;
                pendingRumbleVersion = pendingOutputVersion;
                rumbleAttemptCount = 0;
                rumbleRetryAfterTicks = 0;
                rumbleDeliveryFailed = false;
                outputFlushed.Reset();
            }

            return true;
        }

        public bool TrySetLightbar(byte red, byte green, byte blue)
        {
            if (disposed || !Descriptor.SupportsLightbar)
                return false;

            lock (outputSync)
            {
                if (disposed)
                    return false;

                outputState = outputState with
                {
                    Red = red,
                    Green = green,
                    Blue = blue,
                    HasLightbar = true
                };
                pendingOutputVersion++;
                pendingLightbarVersion = pendingOutputVersion;
                lightbarAttemptCount = 0;
                lightbarRetryAfterTicks = 0;
                lightbarDeliveryFailed = false;
                outputFlushed.Reset();
            }

            return true;
        }

        private void FlushOutputCommands()
        {
            SdlPendingOutputState snapshot;
            long rumbleVersion;
            long lightbarVersion;
            bool flushRumble;
            bool flushLightbar;
            long now = Stopwatch.GetTimestamp();

            lock (outputSync)
            {
                if (pendingOutputVersion == flushedOutputVersion)
                    return;

                snapshot = outputState;
                rumbleVersion = pendingRumbleVersion;
                lightbarVersion = pendingLightbarVersion;
                flushRumble = rumbleVersion > flushedRumbleVersion &&
                              now >= rumbleRetryAfterTicks;
                flushLightbar = lightbarVersion > flushedLightbarVersion &&
                                now >= lightbarRetryAfterTicks;
            }

            if (!flushRumble && !flushLightbar)
                return;

            bool rumbleSucceeded = true;
            bool lightbarSucceeded = true;

            if (flushRumble && snapshot.HasRumble)
            {
                rumbleSucceeded = Sdl3Native.SDL_RumbleGamepad(
                    gamepad,
                    (ushort)(snapshot.LeftMotor * 257),
                    (ushort)(snapshot.RightMotor * 257),
                    snapshot.DurationMs);

                if (!rumbleSucceeded)
                {
                    Debug.WriteLine(
                        $"SDL titreşim gönderilemedi: {Sdl3Native.GetError()}");
                }
            }

            if (flushLightbar && snapshot.HasLightbar)
            {
                lightbarSucceeded = Sdl3Native.SDL_SetGamepadLED(
                    gamepad,
                    snapshot.Red,
                    snapshot.Green,
                    snapshot.Blue);

                if (!lightbarSucceeded)
                {
                    Debug.WriteLine(
                        $"SDL LED gönderilemedi: {Sdl3Native.GetError()}");
                }
            }

            lock (outputSync)
            {
                if (flushRumble &&
                    pendingRumbleVersion == rumbleVersion &&
                    rumbleSucceeded)
                {
                    flushedRumbleVersion = Math.Max(
                        flushedRumbleVersion,
                        rumbleVersion);
                    rumbleAttemptCount = 0;
                    rumbleRetryAfterTicks = 0;
                    rumbleDeliveryFailed = false;
                }
                else if (flushRumble &&
                         pendingRumbleVersion == rumbleVersion)
                {
                    rumbleAttemptCount++;

                    if (rumbleAttemptCount >= MaxOutputAttempts)
                    {
                        flushedRumbleVersion = Math.Max(
                            flushedRumbleVersion,
                            rumbleVersion);
                        rumbleDeliveryFailed = true;
                        rumbleRetryAfterTicks = 0;
                    }
                    else
                    {
                        rumbleRetryAfterTicks = RetryAfter(now);
                    }
                }

                if (flushLightbar &&
                    pendingLightbarVersion == lightbarVersion &&
                    lightbarSucceeded)
                {
                    flushedLightbarVersion = Math.Max(
                        flushedLightbarVersion,
                        lightbarVersion);
                    lightbarAttemptCount = 0;
                    lightbarRetryAfterTicks = 0;
                    lightbarDeliveryFailed = false;
                }
                else if (flushLightbar &&
                         pendingLightbarVersion == lightbarVersion)
                {
                    lightbarAttemptCount++;

                    if (lightbarAttemptCount >= MaxOutputAttempts)
                    {
                        flushedLightbarVersion = Math.Max(
                            flushedLightbarVersion,
                            lightbarVersion);
                        lightbarDeliveryFailed = true;
                        lightbarRetryAfterTicks = 0;
                    }
                    else
                    {
                        lightbarRetryAfterTicks = RetryAfter(now);
                    }
                }

                if (flushedRumbleVersion >= pendingRumbleVersion &&
                    flushedLightbarVersion >= pendingLightbarVersion)
                {
                    flushedOutputVersion = pendingOutputVersion;
                    outputFlushed.Set();
                }
            }
        }

        public bool WaitForPendingOutput(int timeoutMs)
        {
            if (disposed)
                return true;

            if (!outputFlushed.Wait(Math.Max(0, timeoutMs)))
                return false;

            lock (outputSync)
                return !rumbleDeliveryFailed && !lightbarDeliveryFailed;
        }

        private static long RetryAfter(long now)
        {
            return now +
                   (long)(OutputRetryDelayMs *
                          (double)Stopwatch.Frequency / 1000.0);
        }

        private void UpdateBattery()
        {
            int powerState = Sdl3Native.SDL_GetGamepadPowerInfo(
                gamepad,
                out int percentage);

            lastBatteryPercentage =
                powerState >= 0 && percentage is >= 0 and <= 100
                    ? percentage
                    : null;
        }

        private byte ReadStick(int axis)
        {
            return SdlGamepadValueConverter.StickToByte(
                Sdl3Native.SDL_GetGamepadAxis(gamepad, axis));
        }

        private byte ReadTrigger(int axis)
        {
            return SdlGamepadValueConverter.TriggerToByte(
                Sdl3Native.SDL_GetGamepadAxis(gamepad, axis));
        }

        private bool ReadButton(SdlGamepadButton button)
        {
            return Sdl3Native.SDL_GetGamepadButton(
                gamepad,
                (int)button);
        }

        private void AddButton(
            PhysicalGamepadState state,
            SdlGamepadButton button,
            string canonicalName)
        {
            if (ReadButton(button))
                state.Buttons.Add(canonicalName);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            outputFlushed.Set();
            Sdl3Native.SDL_CloseGamepad(gamepad);
        }

        private static PhysicalConnectionType ToConnectionType(int state)
        {
            return state switch
            {
                1 => PhysicalConnectionType.USB,
                2 => PhysicalConnectionType.Bluetooth,
                _ => PhysicalConnectionType.Unknown
            };
        }
    }

    private enum SdlGamepadButton
    {
        South = 0,
        East = 1,
        West = 2,
        North = 3,
        Back = 4,
        Guide = 5,
        Start = 6,
        LeftStick = 7,
        RightStick = 8,
        LeftShoulder = 9,
        RightShoulder = 10,
        DpadUp = 11,
        DpadDown = 12,
        DpadLeft = 13,
        DpadRight = 14,
        Touchpad = 20
    }

    private readonly record struct SdlPendingOutputState(
        byte LeftMotor,
        byte RightMotor,
        uint DurationMs,
        byte Red,
        byte Green,
        byte Blue,
        bool HasRumble,
        bool HasLightbar);
}

internal static class SdlGamepadRuntime
{
    private static readonly object Sync = new();
    private static int referenceCount;
    private static bool initialized;

    public static bool TryAcquire()
    {
        lock (Sync)
        {
            if (initialized)
            {
                referenceCount++;
                return true;
            }

            try
            {
                SetOverrideHint("SDL_JOYSTICK_HIDAPI", "1");
                SetOverrideHint(
                    "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS",
                    "1");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_PS4", "0");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_SWITCH", "1");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_JOY_CONS", "1");
                SetOverrideHint(
                    "SDL_JOYSTICK_HIDAPI_COMBINE_JOY_CONS",
                    "1");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_STEAM", "0");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_STEAMDECK", "0");
                SetOverrideHint("SDL_JOYSTICK_HIDAPI_STEAM_HORI", "0");
                SetOverrideHint("SDL_JOYSTICK_GAMEINPUT", "0");
                SetOverrideHint("SDL_JOYSTICK_WGI", "0");
                SetOverrideHint("SDL_JOYSTICK_RAWINPUT", "0");
                SetOverrideHint("SDL_XINPUT_ENABLED", "1");
                SetOverrideHint("SDL_JOYSTICK_DIRECTINPUT", "1");
                SetOverrideHint("SDL_JOYSTICK_ENHANCED_REPORTS", "1");

                if (!Sdl3Native.SDL_InitSubSystem(Sdl3Native.InitGamepad))
                {
                    Debug.WriteLine(
                        $"SDL gamepad başlatılamadı: {Sdl3Native.GetError()}");
                    return false;
                }

                initialized = true;
                referenceCount = 1;
                Sdl3Native.SDL_SetGamepadEventsEnabled(0);
                return true;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException or
                BadImageFormatException or
                EntryPointNotFoundException)
            {
                Debug.WriteLine($"SDL3 yüklenemedi: {ex.Message}");
                return false;
            }
        }
    }

    public static void Release()
    {
        lock (Sync)
        {
            if (!initialized || referenceCount <= 0)
                return;

            referenceCount--;

            if (referenceCount != 0)
                return;

            Sdl3Native.SDL_QuitSubSystem(Sdl3Native.InitGamepad);
            initialized = false;
        }
    }

    private static void SetOverrideHint(string name, string value)
    {
        const int SdlHintOverride = 2;
        Sdl3Native.SDL_SetHintWithPriority(
            name,
            value,
            SdlHintOverride);
    }
}
