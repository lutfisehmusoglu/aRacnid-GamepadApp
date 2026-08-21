using System.Diagnostics;

namespace GamepadApp.Services;

public enum Ds4InputTransport
{
    Usb,
    Bluetooth
}

public static class Ds4ReportParser
{
    // DS4 touchpad verisi ortak payload'ın sonunda yer alır. USB (64 bayt,
    // 0x01) ve Bluetooth enhanced (78 bayt, 0x11) raporlarında payload
    // başlangıç ofseti farklıdır; aşağıdaki sabitler bu ortak payload'ın
    // sonuna göredir ve parser'ın hesapladığı 'offset' üzerine eklenir.
    // Kaynak yapı (Linux hid-playstation.c): num_touch_reports,
    // ardından her biri 9 bayt (timestamp + 2 x 4 bayt touch point) olan
    // touch_report kayıtları. Her touch point: contact (bit7 active-low),
    // x_lo, [x_hi:4 | y_lo:4], y_hi. X = (x_hi << 8) | x_lo (0-1919),
    // Y = (y_hi << 4) | y_lo (0-942).
    private const int TouchReportCountOffset = 33;
    private const int TouchReportBaseOffset = 34;
    private const int TouchReportStride = 9;
    private const int TouchPoint1Offset = 1;
    private const int TouchPoint2Offset = 5;
    private const byte TouchContactInactiveMask = 0x80;
    private const byte TouchContactIdMask = 0x7F;
    private const int TouchXHighMask = 0x0F;
    private const int TouchYLowShift = 4;
    private const int DebugTouchMoveThreshold = 8;

    private static bool lastDebugTouchActive;
    private static int lastDebugTouchX = -1;
    private static int lastDebugTouchY = -1;

    private static bool lastDebugTouch2Active;
    private static int lastDebugTouch2X = -1;
    private static int lastDebugTouch2Y = -1;

    public static bool TryParse(
        ReadOnlySpan<byte> report,
        Ds4InputTransport transport,
        long sequenceNumber,
        out PhysicalGamepadState state)
    {
        state = new PhysicalGamepadState();

        int offset;
        bool hasBattery;
        bool hasTouch;

        if (transport == Ds4InputTransport.Usb &&
            report.Length >= 64 &&
            report[0] == 0x01)
        {
            offset = 0;
            hasBattery = true;
            hasTouch = true;
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
            hasTouch = true;
        }
        else if (transport == Ds4InputTransport.Bluetooth &&
                 report.Length >= 10 &&
                 report[0] == 0x01)
        {
            // Gerçek DS4 Bluetooth bağlantısı enhanced 0x11 moduna
            // geçmeden 10 baytlık minimal 0x01 input üretebilir. Windows HID
            // bu raporu cihazın 547 baytlık maksimum rapor uzunluğuna sıfırla
            // doldurabilir. Kontrol alanları USB ile aynı başlangıçtadır;
            // minimal framing pil ve touchpad alanlarını içermez.
            offset = 0;
            hasBattery = false;
            hasTouch = false;
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

        if (hasTouch)
            ParseTouch(report, offset, state);

        return true;
    }

    private static void ParseTouch(
        ReadOnlySpan<byte> report,
        int offset,
        PhysicalGamepadState state)
    {
        int countOffset = offset + TouchReportCountOffset;

        if (report.Length <= countOffset)
            return;

        int numTouchReports = report[countOffset];

        if (numTouchReports == 0)
            return;

        // En güncel touch_report kaydı: [0]=timestamp, [1..4]=contact 1,
        // [5..8]=contact 2. İki contact da aynı kayıttan okunur.
        int recordOffset =
            offset +
            TouchReportBaseOffset +
            (numTouchReports - 1) * TouchReportStride;

        ParseTouchPoint(
            report,
            recordOffset + TouchPoint1Offset,
            state,
            isFirst: true);
        ParseTouchPoint(
            report,
            recordOffset + TouchPoint2Offset,
            state,
            isFirst: false);
    }

    private static void ParseTouchPoint(
        ReadOnlySpan<byte> report,
        int contactOffset,
        PhysicalGamepadState state,
        bool isFirst)
    {
        if (report.Length <= contactOffset + 3)
            return;

        byte contact = report[contactOffset];
        byte xLow = report[contactOffset + 1];
        byte xyHigh = report[contactOffset + 2];
        byte yHigh = report[contactOffset + 3];

        int trackingId = contact & TouchContactIdMask;

        if ((contact & TouchContactInactiveMask) != 0)
        {
            if (isFirst)
                DebugLogTouch1Released(trackingId);
            else
                DebugLogTouch2Released(trackingId);
            return;
        }

        int x = ((xyHigh & TouchXHighMask) << 8) | xLow;
        int y = (yHigh << TouchYLowShift) | (xyHigh >> TouchYLowShift);

        if (isFirst)
        {
            state.Touch1Active = true;
            state.Touch1X = x;
            state.Touch1Y = y;
            state.Touch1TrackingId = trackingId;
            DebugLogTouch1Moved(x, y, trackingId);
        }
        else
        {
            state.Touch2Active = true;
            state.Touch2X = x;
            state.Touch2Y = y;
            state.Touch2TrackingId = trackingId;
            DebugLogTouch2Moved(x, y, trackingId);
        }
    }

    private static void DebugLogTouch1Released(int trackingId)
    {
        if (!lastDebugTouchActive)
            return;

        lastDebugTouchActive = false;

        Debug.WriteLine(
            $"DS4 Touch1: Active=False " +
            $"X={lastDebugTouchX} Y={lastDebugTouchY} Id={trackingId}");
    }

    private static void DebugLogTouch1Moved(
        int x,
        int y,
        int trackingId)
    {
        bool movedMeaningfully =
            Math.Abs(x - lastDebugTouchX) >= DebugTouchMoveThreshold ||
            Math.Abs(y - lastDebugTouchY) >= DebugTouchMoveThreshold;

        if (lastDebugTouchActive && !movedMeaningfully)
            return;

        lastDebugTouchActive = true;
        lastDebugTouchX = x;
        lastDebugTouchY = y;

        Debug.WriteLine(
            $"DS4 Touch1: Active=True X={x} Y={y} Id={trackingId}");
    }

    private static void DebugLogTouch2Released(int trackingId)
    {
        if (!lastDebugTouch2Active)
            return;

        lastDebugTouch2Active = false;

        Debug.WriteLine(
            $"DS4 Touch2: Active=False " +
            $"X={lastDebugTouch2X} Y={lastDebugTouch2Y} Id={trackingId}");
    }

    private static void DebugLogTouch2Moved(
        int x,
        int y,
        int trackingId)
    {
        bool movedMeaningfully =
            Math.Abs(x - lastDebugTouch2X) >= DebugTouchMoveThreshold ||
            Math.Abs(y - lastDebugTouch2Y) >= DebugTouchMoveThreshold;

        if (lastDebugTouch2Active && !movedMeaningfully)
            return;

        lastDebugTouch2Active = true;
        lastDebugTouch2X = x;
        lastDebugTouch2Y = y;

        Debug.WriteLine(
            $"DS4 Touch2: Active=True X={x} Y={y} Id={trackingId}");
    }

    public static bool IsWirelessAdapterDisconnected(
        ReadOnlySpan<byte> report)
    {
        return report.Length >= 64 &&
               report[0] == 0x01 &&
               (report[31] & 0x04) != 0;
    }
}
