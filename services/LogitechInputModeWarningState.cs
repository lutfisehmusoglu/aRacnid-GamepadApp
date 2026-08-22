namespace GamepadApp.Services;

public enum LogitechInputMode
{
    None,
    XInput,
    DirectInput
}

public enum LogitechWarningTransition
{
    None,
    ShowXInputWarning,
    DirectInputActivated
}

public readonly record struct LogitechInputModeStatus(
    PhysicalControllerType ControllerType,
    LogitechInputMode InputMode)
{
    public string ModelName => ControllerType switch
    {
        PhysicalControllerType.LogitechF310 => "Logitech F310",
        PhysicalControllerType.LogitechF510 => "Logitech F510",
        PhysicalControllerType.LogitechF710 => "Logitech F710",
        _ => string.Empty
    };
}

public static class LogitechInputModeDetector
{
    private const ushort LogitechVendorId = 0x046D;

    public static LogitechInputModeStatus Detect(
        PhysicalGamepadDescriptor? descriptor)
    {
        if (descriptor == null || descriptor.VendorId != LogitechVendorId)
            return default;

        return descriptor.ProductId switch
        {
            0xC21D => XInput(PhysicalControllerType.LogitechF310),
            0xC21E => XInput(PhysicalControllerType.LogitechF510),
            0xC21F => XInput(PhysicalControllerType.LogitechF710),
            0xC216 => DirectInput(PhysicalControllerType.LogitechF310),
            0xC218 => DirectInput(PhysicalControllerType.LogitechF510),
            0xC219 => DirectInput(PhysicalControllerType.LogitechF710),
            _ => default
        };
    }

    private static LogitechInputModeStatus XInput(
        PhysicalControllerType controllerType) =>
        new(controllerType, LogitechInputMode.XInput);

    private static LogitechInputModeStatus DirectInput(
        PhysicalControllerType controllerType) =>
        new(controllerType, LogitechInputMode.DirectInput);
}

public sealed class LogitechInputModeWarningState
{
    public bool IsWarningActive { get; private set; }
    public PhysicalControllerType ActiveControllerType { get; private set; }
    public string ActiveModelName =>
        new LogitechInputModeStatus(
            ActiveControllerType,
            LogitechInputMode.XInput).ModelName;

    public LogitechWarningTransition Observe(
        PhysicalGamepadDescriptor? descriptor)
    {
        LogitechInputModeStatus status =
            LogitechInputModeDetector.Detect(descriptor);

        if (status.InputMode == LogitechInputMode.XInput)
        {
            bool isNewWarning =
                !IsWarningActive ||
                ActiveControllerType != status.ControllerType;

            IsWarningActive = true;
            ActiveControllerType = status.ControllerType;

            return isNewWarning
                ? LogitechWarningTransition.ShowXInputWarning
                : LogitechWarningTransition.None;
        }

        if (status.InputMode == LogitechInputMode.DirectInput &&
            IsWarningActive &&
            ActiveControllerType == status.ControllerType)
        {
            IsWarningActive = false;
            ActiveControllerType = PhysicalControllerType.Unknown;
            return LogitechWarningTransition.DirectInputActivated;
        }

        return LogitechWarningTransition.None;
    }
}
