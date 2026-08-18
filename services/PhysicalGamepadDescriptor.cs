namespace GamepadApp.Services;

public enum PhysicalControllerType
{
    Unknown,
    DualShock4,
    DualSense,
    DualSenseEdge,
    NintendoSwitchPro,
    NintendoJoyCon,
    LogitechF310,
    LogitechF510,
    LogitechF710
}

public enum PhysicalConnectionType
{
    Unknown,
    USB,
    Bluetooth,
    WirelessReceiver
}

public sealed record PhysicalGamepadDescriptor(
    string DeviceId,
    string DisplayName,
    PhysicalControllerType ControllerType,
    PhysicalConnectionType ConnectionType,
    ushort VendorId,
    ushort ProductId,
    bool SupportsRumble,
    bool SupportsLightbar)
{
    public string ConnectionDisplayName => ConnectionType switch
    {
        PhysicalConnectionType.USB => "USB",
        PhysicalConnectionType.Bluetooth => "Bluetooth",
        PhysicalConnectionType.WirelessReceiver => "Wireless",
        _ => "—"
    };
}
