using System.Diagnostics;

namespace GamepadApp.Services;

public enum Ds4InputTransport
{
    Usb,
    Bluetooth
}

public static class Ds4ReportParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> report,
        Ds4InputTransport transport,
        long sequenceNumber,
        out PhysicalGamepadState state)
    {
        state = new PhysicalGamepadState();

        int offset;
        bool hasBattery;

        if (transport == Ds4InputTransport.Usb &&
            report.Length >= 64 &&
            report[0] == 0x01)
        {
            offset = 0;
            hasBattery = true;
        }
        else if (transport == Ds4InputTransport.Bluetooth &&
                 report.Length >= 78 &&
                 report[0] == 0x11)
        {
            if ((report[1] & 0x80) == 0 ||
                !Ds4OutputReportBuilder.IsBluetoothCrcValid(
                    report.Slice(0, 78),
                    0xA1))
            {
                return false;
            }

            // DS4 Bluetooth 0x11 raporunda standart DS4 payload'ı
            // iki bayt sonra başlar. USB ile aynı parser bu offset
            // üzerinden çalışır.
            offset = 2;
            hasBattery = true;
        }
        else if (transport == Ds4InputTransport.Bluetooth &&
                 report.Length >= 10 &&
                 report[0] == 0x01)
        {
            // Gerçek DS4 Bluetooth bağlantısı enhanced 0x11 moduna
            // geçmeden 10 baytlık minimal 0x01 input üretebilir. Windows HID
            // bu raporu cihazın 547 baytlık maksimum rapor uzunluğuna sıfırla
            // doldurabilir. Kontrol alanları USB ile aynı başlangıçtadır;
            // minimal framing pil alanını içermez.
            offset = 0;
            hasBattery = false;
        }
        else
        {
            return false;
        }

        if (report.Length <= offset + 9)
            return false;

        state.IsConnected = true;
        state.SequenceNumber = sequenceNumber;
        state.TimestampTicks = Stopwatch.GetTimestamp();
        state.LeftStickX = report[offset + 1];
        state.LeftStickY = report[offset + 2];
        state.RightStickX = report[offset + 3];
        state.RightStickY = report[offset + 4];
        state.LeftTrigger = report[offset + 8];
        state.RightTrigger = report[offset + 9];

        byte faceAndDpad = report[offset + 5];

        if ((faceAndDpad & 0x20) != 0) state.Buttons.Add("Cross");
        if ((faceAndDpad & 0x40) != 0) state.Buttons.Add("Circle");
        if ((faceAndDpad & 0x10) != 0) state.Buttons.Add("Square");
        if ((faceAndDpad & 0x80) != 0) state.Buttons.Add("Triangle");

        byte shouldersAndMenus = report[offset + 6];

        if ((shouldersAndMenus & 0x01) != 0) state.Buttons.Add("L1");
        if ((shouldersAndMenus & 0x02) != 0) state.Buttons.Add("R1");
        if ((shouldersAndMenus & 0x10) != 0) state.Buttons.Add("Share");
        if ((shouldersAndMenus & 0x20) != 0) state.Buttons.Add("Options");
        if ((shouldersAndMenus & 0x40) != 0) state.Buttons.Add("L3");
        if ((shouldersAndMenus & 0x80) != 0) state.Buttons.Add("R3");

        byte dpad = (byte)(faceAndDpad & 0x0F);

        if (dpad is 0 or 1 or 7) state.Buttons.Add("D-Pad Up");
        if (dpad is 1 or 2 or 3) state.Buttons.Add("D-Pad Right");
        if (dpad is 3 or 4 or 5) state.Buttons.Add("D-Pad Down");
        if (dpad is 5 or 6 or 7) state.Buttons.Add("D-Pad Left");

        byte systemButtons = report[offset + 7];
        state.PsPressed = (systemButtons & 0x01) != 0;
        state.TouchpadPressed = (systemButtons & 0x02) != 0;

        if (hasBattery)
        {
            int batteryLevel = report[offset + 30] & 0x0F;
            state.BatteryPercentage = batteryLevel switch
            {
                <= 10 => batteryLevel * 10,
                11 => 100,
                _ => null
            };
        }

        return true;
    }

    public static bool IsWirelessAdapterDisconnected(
        ReadOnlySpan<byte> report)
    {
        return report.Length >= 64 &&
               report[0] == 0x01 &&
               (report[31] & 0x04) != 0;
    }
}
