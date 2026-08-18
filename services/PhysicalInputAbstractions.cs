namespace GamepadApp.Services;

internal enum PhysicalReadResult
{
    State,
    Timeout,
    Disconnected
}

internal interface IPhysicalGamepadProvider : IDisposable
{
    IPhysicalGamepadSession? TryOpen();
}

internal interface IPhysicalGamepadSession : IDisposable
{
    PhysicalGamepadDescriptor Descriptor { get; }

    PhysicalReadResult ReadNext(out PhysicalGamepadState? state);

    bool TrySetVibration(byte leftMotor, byte rightMotor, uint durationMs);

    bool TrySetLightbar(byte red, byte green, byte blue);

    bool WaitForPendingOutput(int timeoutMs);
}
