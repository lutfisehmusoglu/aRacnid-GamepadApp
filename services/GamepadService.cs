namespace GamepadApp.Services;

public enum VirtualControllerType
{
    DualShock4,
    Xbox360
}

public class GamepadService
{
    public static double DeadzonePercent { get; set; } = 10;
    public static double AntiDeadzonePercent { get; set; }
    public static double SensitivityPercent { get; set; } = 100;

    public static event Action<VirtualControllerType>?
        SelectedVirtualTypeChanged;

    private static VirtualControllerType selectedVirtualType =
        VirtualControllerType.DualShock4;

    public static VirtualControllerType SelectedVirtualType
    {
        get => selectedVirtualType;
        set
        {
            if (selectedVirtualType == value)
                return;

            selectedVirtualType = value;
            SelectedVirtualTypeChanged?.Invoke(value);
        }
    }

    public (byte X, byte Y) ApplyDeadzone(
        byte x,
        byte y,
        double deadzonePercent)
    {
        const double center = 128.0;
        double dx = x - center;
        double dy = y - center;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double deadzoneRadius =
            128.0 * (deadzonePercent / 100.0);

        return distance <= deadzoneRadius
            ? ((byte)128, (byte)128)
            : (x, y);
    }

    public (byte X, byte Y) ApplyAntiDeadzone(
        byte x,
        byte y,
        double antiDeadzonePercent)
    {
        const double center = 128.0;
        double dx = x - center;
        double dy = y - center;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance == 0 || antiDeadzonePercent <= 0)
            return (x, y);

        double antiRadius =
            128.0 * (antiDeadzonePercent / 100.0);

        if (distance >= antiRadius)
            return (x, y);

        double scale = antiRadius / distance;
        int newX = (int)Math.Round(center + dx * scale);
        int newY = (int)Math.Round(center + dy * scale);

        return (
            (byte)Math.Clamp(newX, 0, 255),
            (byte)Math.Clamp(newY, 0, 255));
    }

    public (byte X, byte Y) ApplySensitivity(
        byte x,
        byte y,
        double sensitivityPercent)
    {
        // %100 fiziksel değerin birebir aktarımıdır. Bu erken dönüş özellikle
        // köşe/diagonal değerlerin gereksiz dairesel clamp ile bozulmasını önler.
        if (Math.Abs(sensitivityPercent - 100.0) < 0.0001)
            return (x, y);

        const double center = 128.0;
        double dx = (x - center) * (sensitivityPercent / 100.0);
        double dy = (y - center) * (sensitivityPercent / 100.0);
        double distance = Math.Sqrt(dx * dx + dy * dy);
        const double maxRadius = 127.0;

        if (distance > maxRadius)
        {
            double scale = maxRadius / distance;
            dx *= scale;
            dy *= scale;
        }

        int newX = (int)Math.Round(center + dx);
        int newY = (int)Math.Round(center + dy);

        return (
            (byte)Math.Clamp(newX, 0, 255),
            (byte)Math.Clamp(newY, 0, 255));
    }
}
