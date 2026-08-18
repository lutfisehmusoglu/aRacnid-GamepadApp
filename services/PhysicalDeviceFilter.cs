using HidSharp;

namespace GamepadApp.Services;

public static class PhysicalDeviceFilter
{
    public static bool IsKnownVirtualName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return false;

        return productName.Contains(
                   "Virtual",
                   StringComparison.OrdinalIgnoreCase) ||
               productName.Contains(
                   "ViGEm",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVirtual(HidDevice device)
    {
        string? productName = null;

        try
        {
            productName = device.GetProductName();
        }
        catch
        {
        }

        if (IsKnownVirtualName(productName))
            return true;

        HidDeviceOrigin origin =
            WindowsHidDeviceInspector.ResolveOrigin(device.DevicePath);

        return origin switch
        {
            HidDeviceOrigin.ViGEmVirtual => true,
            HidDeviceOrigin.Physical => false,

            // Gerçek DS4 v1 ve ViGEm DS4 aynı VID/PID'yi taşır.
            // PnP incelemesi geçici olarak başarısızsa feedback loop
            // oluşturmak yerine bu taramada 05C4'ü atlarız.
            HidDeviceOrigin.Unknown when
                device.VendorID == 0x054C &&
                device.ProductID == 0x05C4 => true,

            _ => false
        };
    }
}
