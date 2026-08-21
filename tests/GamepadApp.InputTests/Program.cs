using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamepadApp.Models;
using GamepadApp.Services;
using HidSharp;
using Nefarius.ViGEm.Client;

var tests = new (string Name, Action Body)[]
{
    ("DS4 USB neutral report", TestDs4UsbNeutral),
    ("DS4 USB buttons, axes and battery", TestDs4UsbButtonsAxesBattery),
    ("DS4 USB touchpad parsing", TestDs4UsbTouch),
    ("DS4 dual touch parsing", TestDs4DualTouchParsing),
    ("DS4 Bluetooth normalization", TestDs4BluetoothNormalization),
    ("DS4 Bluetooth touchpad parsing", TestDs4BluetoothTouch),
    ("DS4 Bluetooth padded minimal framing", TestDs4BluetoothMinimalFraming),
    ("DS4 Bluetooth integrity checks", TestDs4BluetoothIntegrity),
    ("DS4 wireless adapter disconnect bit", TestDs4WirelessAdapterDisconnect),
    ("DS4 malformed reports rejected", TestDs4MalformedReports),
    ("DS4 output report layout and CRC", TestDs4OutputReports),
    ("Physical snapshot is atomic", TestPhysicalSnapshotCopy),
    ("SDL stick conversion exhaustive", TestSdlStickConversion),
    ("SDL trigger conversion exhaustive", TestSdlTriggerConversion),
    ("SDL physical allowlist", TestSdlAllowlist),
    ("SDL out-of-scope and virtual rejection", TestSdlRejection),
    ("Mapped digital buttons drive triggers", TestDigitalToTriggerMapping),
    ("Mapped trigger sources accumulate", TestTriggerAccumulation),
    ("Mapped analog trigger drives a button", TestTriggerToButtonMapping),
    ("Low analog trigger values pass through", TestLowTriggerPassthrough),
    ("Stick direction identity is byte exact", TestStickDirectionIdentity),
    ("Stick directions remap to axes and buttons", TestStickDirectionRemapping),
    ("Sensitivity 100 is byte exact", TestSensitivityIdentity),
    ("Game feedback motor scaling", TestMotorStrengthScaling),
    ("DS4 raw rumble feedback parsing", TestDs4RumbleFeedbackParsing),
    ("DS4 touch point packing", TestDs4TouchPointPacking),
    ("DS4 second touch contact inactive", TestDs4SecondTouchContactInactive),
    ("DS4 dual touch report", TestDs4DualTouchReport),
    ("Touch output state carry", TestTouchOutputCarry),
    ("Touchpad mode defaults and round-trip", TestTouchpadModeRoundTrip),
    ("Touchpad mode filters touch output", TestTouchpadModeOutputFilter),
    ("Profile writes recover atomically", TestProfileAtomicRecovery),
    ("Settings writes recover atomically", TestSettingsAtomicRecovery),
    ("Localization JSON files are valid", TestLocalizationJson),
    ("Virtual reports stay single-submit", TestSingleSubmitSourceGuard),
    ("UI has no second HID reader", TestSinglePhysicalReaderSourceGuard),
    ("SDL 3.4.14 native ABI", TestSdlNativeAbi),
};

bool live = args.Contains("--live", StringComparer.OrdinalIgnoreCase);
int passed = 0;

foreach ((string name, Action body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS  {name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL  {name}: {ex.Message}");
    }
}

if (live)
{
    try
    {
        TestLiveVigemFilter();
        Console.WriteLine("PASS  Live ViGEm PnP filter");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"FAIL  Live ViGEm PnP filter: {ex.Message}");
    }
}

int expected = tests.Length + (live ? 1 : 0);
Console.WriteLine($"RESULT {passed}/{expected}");
return passed == expected ? 0 : 1;

static void TestDs4UsbNeutral()
{
    byte[] report = CreateUsbReport();

    Assert(Ds4ReportParser.TryParse(
        report,
        Ds4InputTransport.Usb,
        41,
        out PhysicalGamepadState state));
    Equal(41L, state.SequenceNumber);
    Assert(state.IsConnected);
    Equal((byte)128, state.LeftStickX);
    Equal((byte)128, state.LeftStickY);
    Equal((byte)128, state.RightStickX);
    Equal((byte)128, state.RightStickY);
    Equal((byte)0, state.LeftTrigger);
    Equal((byte)0, state.RightTrigger);
    Equal(0, state.Buttons.Count);
    Assert(!state.PsPressed && !state.TouchpadPressed);
    Assert(!state.Touch1Active);
    Equal(0, state.Touch1X);
    Equal(0, state.Touch1Y);
    Equal(0, state.Touch1TrackingId);
    Equal(50, state.BatteryPercentage);
}

static void TestDs4UsbTouch()
{
    byte[] report = CreateUsbReport();
    report[33] = 1;        // Bu raporda tek touch_report var.
    report[35] = 0x07;     // Contact: active (bit7=0), tracking id 7.
    report[36] = 0xD2;     // X düşük bayt (X=1234 -> 0x04D2).
    report[37] = 0x84;     // x_hi=4 | y_lo=8.
    report[38] = 0x1C;     // Y yüksek bayt (Y=456 -> 0x1C8).

    Assert(Ds4ReportParser.TryParse(
        report,
        Ds4InputTransport.Usb,
        1,
        out PhysicalGamepadState state));

    Assert(state.Touch1Active);
    Equal(1234, state.Touch1X);
    Equal(456, state.Touch1Y);
    Equal(7, state.Touch1TrackingId);

    // Parmak kalktığında contact bit7 setlenir; active-low.
    report[35] = 0x87;
    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Usb, 2, out state));
    Assert(!state.Touch1Active);

    // Birden fazla touch_report varsa en güncel kayıt kullanılır.
    report[35] = 0x00;     // Güncel olmayan kayıt: contact 0 (id 0).
    report[36] = 0x10;     // Eski X.
    report[33] = 2;
    report[44] = 0x05;     // İkinci kayıt point1: contact id 5.
    report[45] = 0x04;     // X=516 -> 0x0204 (x_lo=0x04).
    report[46] = 0x22;     // x_hi=2 | y_lo=2 (Y=322 -> 0x142).
    report[47] = 0x14;
    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Usb, 3, out state));
    Assert(state.Touch1Active);
    Equal(516, state.Touch1X);
    Equal(322, state.Touch1Y);
    Equal(5, state.Touch1TrackingId);

    // num_touch_reports = 0 ise touch verisi yok; parmak inaktif.
    report[33] = 0;
    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Usb, 4, out state));
    Assert(!state.Touch1Active);
}

static void TestDs4DualTouchParsing()
{
    byte[] usb = CreateUsbReport();
    usb[33] = 1;
    usb[35] = 0x07;   // contact1 active id 7.
    usb[36] = 0xD2;   // X1=1234 (0x04D2).
    usb[37] = 0x84;   // x_hi=4 | y_lo=8.
    usb[38] = 0x1C;   // Y1=456 (0x1C8).
    usb[39] = 0x05;   // contact2 active id 5.
    usb[40] = 0x84;   // X2=900 (0x0384).
    usb[41] = 0x43;   // x_hi=3 | y_lo=4.
    usb[42] = 0x1A;   // Y2=420 (0x1A4).

    Assert(Ds4ReportParser.TryParse(
        usb, Ds4InputTransport.Usb, 1, out PhysicalGamepadState state));

    Assert(state.Touch1Active);
    Equal(1234, state.Touch1X);
    Equal(456, state.Touch1Y);
    Equal(7, state.Touch1TrackingId);

    Assert(state.Touch2Active);
    Equal(900, state.Touch2X);
    Equal(420, state.Touch2Y);
    Equal(5, state.Touch2TrackingId);

    // Bluetooth enhanced 0x11: touch alanı 2 bayt ileride.
    byte[] bluetooth = new byte[78];
    bluetooth[0] = 0x11;
    bluetooth[1] = 0x80;
    Array.Copy(usb, 1, bluetooth, 3, usb.Length - 1);
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(bluetooth, 0xA1);

    Assert(Ds4ReportParser.TryParse(
        bluetooth, Ds4InputTransport.Bluetooth, 2, out state));

    Assert(state.Touch1Active);
    Equal(1234, state.Touch1X);
    Equal(456, state.Touch1Y);
    Equal(7, state.Touch1TrackingId);
    Assert(state.Touch2Active);
    Equal(900, state.Touch2X);
    Equal(420, state.Touch2Y);
    Equal(5, state.Touch2TrackingId);

    // Touch2 release: contact2 bit7 set → Touch2 inaktif, Touch1 aktif.
    usb[39] = 0x85;
    Assert(Ds4ReportParser.TryParse(
        usb, Ds4InputTransport.Usb, 3, out state));
    Assert(state.Touch1Active);
    Assert(!state.Touch2Active);
}

static void TestDs4UsbButtonsAxesBattery()
{
    byte[] report = CreateUsbReport();
    report[1] = 1;
    report[2] = 2;
    report[3] = 253;
    report[4] = 254;
    report[5] = 0xF1; // Tüm yüz tuşları + kuzeydoğu.
    report[6] = 0xF3; // L1, R1, Share, Options, L3, R3.
    report[7] = 0x03; // PS + touchpad.
    report[8] = 31;
    report[9] = 255;
    report[30] = 0x0A;

    Assert(Ds4ReportParser.TryParse(
        report,
        Ds4InputTransport.Usb,
        7,
        out PhysicalGamepadState state));
    Equal((byte)1, state.LeftStickX);
    Equal((byte)2, state.LeftStickY);
    Equal((byte)253, state.RightStickX);
    Equal((byte)254, state.RightStickY);
    Equal((byte)31, state.LeftTrigger);
    Equal((byte)255, state.RightTrigger);
    Equal(100, state.BatteryPercentage);
    Assert(state.PsPressed && state.TouchpadPressed);

    string[] expected =
    [
        "Cross", "Circle", "Square", "Triangle",
        "L1", "R1", "Share", "Options", "L3", "R3",
        "D-Pad Up", "D-Pad Right"
    ];

    SetEqual(expected, state.Buttons);

    report[30] = 0x0B;
    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Usb, 8, out state));
    Equal(100, state.BatteryPercentage);

    report[30] = 0x0C;
    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Usb, 9, out state));
    Equal<int?>(null, state.BatteryPercentage);
}

static void TestDs4BluetoothNormalization()
{
    byte[] usb = CreateUsbReport();
    usb[1] = 13;
    usb[2] = 29;
    usb[3] = 201;
    usb[4] = 240;
    usb[5] = 0x25; // Cross + southwest.
    usb[6] = 0x82; // R1 + R3.
    usb[7] = 0x02;
    usb[8] = 88;
    usb[9] = 144;
    usb[30] = 7;

    byte[] bluetooth = new byte[78];
    bluetooth[0] = 0x11;
    bluetooth[1] = 0x80;
    Array.Copy(usb, 1, bluetooth, 3, usb.Length - 1);
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(bluetooth, 0xA1);

    Assert(Ds4ReportParser.TryParse(
        usb,
        Ds4InputTransport.Usb,
        1,
        out PhysicalGamepadState usbState));
    Assert(Ds4ReportParser.TryParse(
        bluetooth,
        Ds4InputTransport.Bluetooth,
        2,
        out PhysicalGamepadState bluetoothState));

    Equal(usbState.LeftStickX, bluetoothState.LeftStickX);
    Equal(usbState.LeftStickY, bluetoothState.LeftStickY);
    Equal(usbState.RightStickX, bluetoothState.RightStickX);
    Equal(usbState.RightStickY, bluetoothState.RightStickY);
    Equal(usbState.LeftTrigger, bluetoothState.LeftTrigger);
    Equal(usbState.RightTrigger, bluetoothState.RightTrigger);
    Equal(usbState.BatteryPercentage, bluetoothState.BatteryPercentage);
    Equal(usbState.PsPressed, bluetoothState.PsPressed);
    Equal(usbState.TouchpadPressed, bluetoothState.TouchpadPressed);
    SetEqual(usbState.Buttons, bluetoothState.Buttons);
}

static void TestDs4BluetoothTouch()
{
    byte[] bluetooth = new byte[78];
    bluetooth[0] = 0x11;
    bluetooth[1] = 0x80;
    bluetooth[35] = 1;     // num_touch_reports (offset 2 + 33).
    bluetooth[37] = 0x03;  // Contact: active, tracking id 3.
    bluetooth[38] = 0xE8;  // X=1000 -> 0x03E8 (x_lo=0xE8).
    bluetooth[39] = 0x33;  // x_hi=3 | y_lo=3 (Y=787 -> 0x313).
    bluetooth[40] = 0x31;  // Y yüksek bayt.
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(bluetooth, 0xA1);

    Assert(Ds4ReportParser.TryParse(
        bluetooth,
        Ds4InputTransport.Bluetooth,
        1,
        out PhysicalGamepadState state));

    Assert(state.Touch1Active);
    Equal(1000, state.Touch1X);
    Equal(787, state.Touch1Y);
    Equal(3, state.Touch1TrackingId);

    // Minimal 0x01 framing touch alanı içermez; güvenli default.
    byte[] minimal = new byte[547];
    minimal[0] = 0x01;
    minimal[5] = 0x08;
    Assert(Ds4ReportParser.TryParse(
        minimal, Ds4InputTransport.Bluetooth, 2, out state));
    Assert(!state.Touch1Active);
    Equal(0, state.Touch1X);
    Equal(0, state.Touch1Y);
}

static void TestDs4BluetoothMinimalFraming()
{
    // Gerçek HidSharp okumasından gözlenen framing: 10 baytlık 0x01 input,
    // cihazın maksimum input rapor uzunluğu olan 547 bayta sıfır padding.
    byte[] report = new byte[547];
    report[0] = 0x01;
    report[1] = 128;
    report[2] = 127;
    report[3] = 128;
    report[4] = 121;
    report[5] = 0x28; // Cross + neutral D-pad.
    report[6] = 0x02; // R1.
    report[7] = 0x03; // PS + touchpad.
    report[8] = 17;
    report[9] = 231;

    Assert(Ds4ReportParser.TryParse(
        report,
        Ds4InputTransport.Bluetooth,
        12,
        out PhysicalGamepadState state));

    Equal(12L, state.SequenceNumber);
    Equal((byte)128, state.LeftStickX);
    Equal((byte)127, state.LeftStickY);
    Equal((byte)128, state.RightStickX);
    Equal((byte)121, state.RightStickY);
    Equal((byte)17, state.LeftTrigger);
    Equal((byte)231, state.RightTrigger);
    Equal<int?>(null, state.BatteryPercentage);
    Assert(state.PsPressed && state.TouchpadPressed);
    SetEqual(new[] { "Cross", "R1" }, state.Buttons);
}

static void TestDs4BluetoothIntegrity()
{
    byte[] report = new byte[78];
    report[0] = 0x11;
    report[1] = 0x80;
    report[3] = 128;
    report[4] = 128;
    report[5] = 128;
    report[6] = 128;
    report[7] = 0x08;
    report[32] = 5;
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(report, 0xA1);

    Assert(Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Bluetooth, 1, out _));

    report[0] = 0x19;
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(report, 0xA1);
    Assert(!Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Bluetooth, 2, out _));

    report[0] = 0x11;
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(report, 0xA1);

    report[10] ^= 0x01;
    Assert(!Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Bluetooth, 3, out _));

    report[10] ^= 0x01;
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(report, 0xA1);
    report[1] &= 0x7F;
    Ds4OutputReportBuilder.FinalizeBluetoothCrc(report, 0xA1);
    Assert(!Ds4ReportParser.TryParse(
        report, Ds4InputTransport.Bluetooth, 4, out _));

    byte[] paddedMinimalBluetooth = new byte[9];
    paddedMinimalBluetooth[0] = 0x01;
    Assert(!Ds4ReportParser.TryParse(
        paddedMinimalBluetooth,
        Ds4InputTransport.Bluetooth,
        5,
        out _));
}

static void TestDs4WirelessAdapterDisconnect()
{
    byte[] report = CreateUsbReport();
    report[31] = 0x04;
    Assert(Ds4ReportParser.IsWirelessAdapterDisconnected(report));

    report[31] = 0x00;
    Assert(!Ds4ReportParser.IsWirelessAdapterDisconnected(report));
    Assert(!Ds4ReportParser.IsWirelessAdapterDisconnected(new byte[20]));
}

static void TestDs4MalformedReports()
{
    Assert(!Ds4ReportParser.TryParse(
        [], Ds4InputTransport.Usb, 0, out _));
    Assert(!Ds4ReportParser.TryParse(
        new byte[31], Ds4InputTransport.Usb, 0, out _));

    byte[] unknown = new byte[64];
    unknown[0] = 0x31;
    Assert(!Ds4ReportParser.TryParse(
        unknown, Ds4InputTransport.Usb, 0, out _));

    byte[] shortBluetooth = new byte[32];
    shortBluetooth[0] = 0x11;
    Assert(!Ds4ReportParser.TryParse(
        shortBluetooth, Ds4InputTransport.Bluetooth, 0, out _));

    byte[] truncatedUsb = new byte[63];
    truncatedUsb[0] = 0x01;
    Assert(!Ds4ReportParser.TryParse(
        truncatedUsb, Ds4InputTransport.Usb, 0, out _));
}

static void TestDs4OutputReports()
{
    var rumbleOnly = new Ds4OutputState(
        LeftMotor: 200,
        RightMotor: 100,
        Red: 0,
        Green: 0,
        Blue: 0,
        HasRumble: true,
        HasLightbar: false);
    byte[] usbRumble = Ds4OutputReportBuilder.BuildUsb(rumbleOnly);
    Equal(32, usbRumble.Length);
    Equal((byte)0x05, usbRumble[0]);
    Equal((byte)0x01, usbRumble[1]);
    Equal((byte)100, usbRumble[4]);
    Equal((byte)200, usbRumble[5]);

    var combined = rumbleOnly with
    {
        Red = 11,
        Green = 22,
        Blue = 33,
        HasLightbar = true
    };
    byte[] usbCombined = Ds4OutputReportBuilder.BuildUsb(combined);
    Equal((byte)0x03, usbCombined[1]);
    Equal((byte)11, usbCombined[6]);
    Equal((byte)22, usbCombined[7]);
    Equal((byte)33, usbCombined[8]);

    byte[] bluetooth = Ds4OutputReportBuilder.BuildBluetooth(combined);
    Equal(78, bluetooth.Length);
    Equal((byte)0x11, bluetooth[0]);
    Equal((byte)0xC4, bluetooth[1]);
    Equal((byte)0x03, bluetooth[3]);
    Equal((byte)0x00, bluetooth[4]);
    Equal((byte)0x00, bluetooth[5]);
    Equal((byte)100, bluetooth[6]);
    Equal((byte)200, bluetooth[7]);
    Equal((byte)11, bluetooth[8]);
    Equal((byte)22, bluetooth[9]);
    Equal((byte)33, bluetooth[10]);
    Assert(Ds4OutputReportBuilder.IsBluetoothCrcValid(bluetooth, 0xA2));

    bluetooth[8] ^= 0x01;
    Assert(!Ds4OutputReportBuilder.IsBluetoothCrcValid(bluetooth, 0xA2));
}

static void TestPhysicalSnapshotCopy()
{
    var source = new PhysicalGamepadState
    {
        IsConnected = true,
        SequenceNumber = 9,
        TimestampTicks = 123,
        LeftStickX = 1,
        LeftStickY = 2,
        RightStickX = 3,
        RightStickY = 4,
        LeftTrigger = 5,
        RightTrigger = 6,
        PsPressed = true,
        TouchpadPressed = true,
        Touch1Active = true,
        Touch1X = 111,
        Touch1Y = 222,
        Touch1TrackingId = 3,
        Touch2Active = true,
        Touch2X = 333,
        Touch2Y = 444,
        Touch2TrackingId = 4,
        BatteryPercentage = 70
    };
    source.Buttons.Add("Cross");

    var target = new PhysicalGamepadState();
    target.Buttons.Add("Circle");
    target.CopyFrom(source);

    Equal(9L, target.SequenceNumber);
    Equal(123L, target.TimestampTicks);
    Equal((byte)1, target.LeftStickX);
    Equal((byte)6, target.RightTrigger);
    Equal(111, target.Touch1X);
    Equal(222, target.Touch1Y);
    Equal(3, target.Touch1TrackingId);
    Equal(333, target.Touch2X);
    Equal(444, target.Touch2Y);
    Equal(4, target.Touch2TrackingId);
    SetEqual(["Cross"], target.Buttons);

    source.Buttons.Clear();
    Assert(target.Buttons.Contains("Cross"));

    PhysicalGamepadState clone = target.Clone();
    target.Reset();
    Assert(!target.IsConnected && target.Buttons.Count == 0);
    Assert(!target.Touch1Active && !target.Touch2Active);
    Assert(clone.IsConnected && clone.Buttons.Contains("Cross"));
    Assert(clone.Touch1Active && clone.Touch2Active);
}

static void TestSdlStickConversion()
{
    Equal((byte)0, SdlGamepadValueConverter.StickToByte(short.MinValue));
    Equal((byte)128, SdlGamepadValueConverter.StickToByte(0));
    Equal((byte)255, SdlGamepadValueConverter.StickToByte(short.MaxValue));

    byte previous = 0;
    for (int value = short.MinValue; value <= short.MaxValue; value++)
    {
        byte converted = SdlGamepadValueConverter.StickToByte((short)value);
        Assert(converted >= previous, $"Stick conversion fell at {value}.");
        previous = converted;
    }
}

static void TestSdlTriggerConversion()
{
    Equal((byte)0, SdlGamepadValueConverter.TriggerToByte(short.MinValue));
    Equal((byte)0, SdlGamepadValueConverter.TriggerToByte(0));
    Equal((byte)255, SdlGamepadValueConverter.TriggerToByte(short.MaxValue));

    byte previous = 0;
    for (int value = short.MinValue; value <= short.MaxValue; value++)
    {
        byte converted = SdlGamepadValueConverter.TriggerToByte((short)value);
        Assert(converted >= previous, $"Trigger conversion fell at {value}.");
        previous = converted;
    }
}

static void TestSdlAllowlist()
{
    var expected = new (ushort Vid, ushort Pid, PhysicalControllerType Type)[]
    {
        (0x054C, 0x0CE6, PhysicalControllerType.DualSense),
        (0x054C, 0x0DF2, PhysicalControllerType.DualSenseEdge),
        (0x057E, 0x2009, PhysicalControllerType.NintendoSwitchPro),
        (0x057E, 0x2006, PhysicalControllerType.NintendoJoyCon),
        (0x057E, 0x2007, PhysicalControllerType.NintendoJoyCon),
        (0x057E, 0x2008, PhysicalControllerType.NintendoJoyCon),
        (0x057E, 0x200E, PhysicalControllerType.NintendoJoyCon),
        (0x046D, 0xC216, PhysicalControllerType.LogitechF310),
        (0x046D, 0xC21D, PhysicalControllerType.LogitechF310),
        (0x046D, 0xC218, PhysicalControllerType.LogitechF510),
        (0x046D, 0xC21E, PhysicalControllerType.LogitechF510),
        (0x046D, 0xC219, PhysicalControllerType.LogitechF710),
        (0x046D, 0xC21F, PhysicalControllerType.LogitechF710),
    };

    foreach ((ushort vid, ushort pid, PhysicalControllerType type) in expected)
    {
        // Logitech X modu SDL tarafından Xbox 360 tipi (2) bildirilse de
        // exact VID/PID kabul listesi belirleyicidir.
        Assert(SdlGamepadClassifier.TryClassify(vid, pid, 2, out var item));
        Equal(type, item.ControllerType);
    }
}

static void TestSdlRejection()
{
    var rejected = new (ushort Vid, ushort Pid, int Type)[]
    {
        (0x045E, 0x028E, 2), // ViGEm / fiziksel Xbox 360; kapsam dışı.
        (0x045E, 0x02EA, 3), // Xbox One; kapsam dışı.
        (0x054C, 0x05C4, 5), // DS4 v1 / ViGEm DS4; raw sağlayıcıya ait.
        (0x054C, 0x09CC, 5), // DS4 v2; raw sağlayıcıya ait.
        (0x28DE, 0x1102, 0), // Steam Controller; kapsam dışı.
        (0x046D, 0xC20A, 2), // Kabul listesinde olmayan Logitech.
        (0x1234, 0x5678, 1),
        (0x0000, 0x0000, 0),
    };

    foreach ((ushort vid, ushort pid, int type) in rejected)
    {
        Assert(!SdlGamepadClassifier.TryClassify(vid, pid, type, out _));
    }
}

static void TestDigitalToTriggerMapping()
{
    var settings = new ControllerProfileSettings();
    settings.ButtonMappings["Cross"] = "L2";
    settings.ButtonMappings["Circle"] = "R2";
    var output = new GamepadOutputState();
    var remap = new ButtonRemapService();

    remap.ApplyMappedInput(settings, "Cross", 255, output);
    remap.ApplyMappedInput(settings, "Circle", 255, output);

    Equal((byte)255, output.LeftTrigger);
    Equal((byte)255, output.RightTrigger);
    Assert(!output.Buttons.Contains("L2") && !output.Buttons.Contains("R2"));

    output = new GamepadOutputState();
    Equal((byte)0, output.LeftTrigger);
    Equal((byte)0, output.RightTrigger);
}

static void TestTriggerAccumulation()
{
    var settings = new ControllerProfileSettings();
    settings.ButtonMappings["Cross"] = "L2";
    settings.ButtonMappings["L2"] = "R2";
    settings.ButtonMappings["R2"] = "R2";
    var output = new GamepadOutputState();
    var remap = new ButtonRemapService();

    remap.ApplyMappedInput(settings, "Cross", 255, output);
    remap.ApplyMappedInput(settings, "L2", 90, output, 30);
    remap.ApplyMappedInput(settings, "R2", 180, output, 30);

    Equal((byte)255, output.LeftTrigger);
    Equal((byte)180, output.RightTrigger);

    output = new GamepadOutputState();
    remap.ApplyMappedInput(settings, "R2", 180, output, 30);
    remap.ApplyMappedInput(settings, "L2", 90, output, 30);
    Equal((byte)180, output.RightTrigger);
}

static void TestTriggerToButtonMapping()
{
    var settings = new ControllerProfileSettings();
    settings.ButtonMappings["L2"] = "Cross";
    var remap = new ButtonRemapService();

    var atThreshold = new GamepadOutputState();
    remap.ApplyMappedInput(settings, "L2", 30, atThreshold, 30);
    Assert(!atThreshold.Buttons.Contains("Cross"));

    var aboveThreshold = new GamepadOutputState();
    remap.ApplyMappedInput(settings, "L2", 31, aboveThreshold, 30);
    Assert(aboveThreshold.Buttons.Contains("Cross"));
}

static void TestLowTriggerPassthrough()
{
    var settings = new ControllerProfileSettings();
    var remap = new ButtonRemapService();
    var output = new GamepadOutputState();

    remap.ApplyMappedInput(settings, "L2", 1, output, 30);
    remap.ApplyMappedInput(settings, "R2", 30, output, 30);

    Equal((byte)1, output.LeftTrigger);
    Equal((byte)30, output.RightTrigger);
}

static void TestStickDirectionIdentity()
{
    var settings = new ControllerProfileSettings();
    var remap = new ButtonRemapService();

    for (int value = byte.MinValue; value <= byte.MaxValue; value++)
    {
        var xOutput = new GamepadOutputState();
        if (value < 128)
        {
            remap.ApplyMappedStickDirection(
                settings, "LS X-", 128 - value, 128, xOutput);
        }
        else if (value > 128)
        {
            remap.ApplyMappedStickDirection(
                settings, "LS X+", value - 128, 127, xOutput);
        }

        Equal((byte)value, xOutput.LeftStickX);

        var yOutput = new GamepadOutputState();
        if (value < 128)
        {
            remap.ApplyMappedStickDirection(
                settings, "RS Y+", 128 - value, 128, yOutput);
        }
        else if (value > 128)
        {
            remap.ApplyMappedStickDirection(
                settings, "RS Y-", value - 128, 127, yOutput);
        }

        Equal((byte)value, yOutput.RightStickY);
    }
}

static void TestStickDirectionRemapping()
{
    var settings = new ControllerProfileSettings();
    settings.ButtonMappings["LS X+"] = "RS Y+";
    settings.ButtonMappings["LS Y+"] = "Circle";
    settings.ButtonMappings["Cross"] = "LS X-";
    var remap = new ButtonRemapService();
    var output = new GamepadOutputState();

    remap.ApplyMappedStickDirection(
        settings, "LS X+", 127, 127, output);
    Equal((byte)0, output.RightStickY);

    remap.ApplyMappedStickDirection(
        settings, "LS Y+", 64, 128, output);
    Assert(output.Buttons.Contains("Circle"));

    remap.ApplyMappedInput(settings, "Cross", 255, output);
    Equal((byte)0, output.LeftStickX);
}

static void TestSensitivityIdentity()
{
    var gamepad = new GamepadService();

    for (int x = byte.MinValue; x <= byte.MaxValue; x++)
    {
        for (int y = byte.MinValue; y <= byte.MaxValue; y++)
        {
            var result = gamepad.ApplySensitivity((byte)x, (byte)y, 100);
            Equal((byte)x, result.X);
            Equal((byte)y, result.Y);
        }
    }
}

static void TestMotorStrengthScaling()
{
    Equal((byte)0, GamepadEmulationService.ScaleMotorStrength(255, 0));
    Equal((byte)128, GamepadEmulationService.ScaleMotorStrength(255, 50));
    Equal((byte)255, GamepadEmulationService.ScaleMotorStrength(255, 100));
    Equal((byte)64, GamepadEmulationService.ScaleMotorStrength(64, 150));
    Equal((byte)0, GamepadEmulationService.ScaleMotorStrength(64, -10));
}

static void TestDs4RumbleFeedbackParsing()
{
    byte[] report = new byte[64];
    report[0] = 0x05;
    report[1] = 0x01;
    report[4] = 70;
    report[5] = 180;

    Assert(VirtualGamepadService.TryParseDs4RumbleFeedback(
        report,
        out byte large,
        out byte small));
    Equal((byte)180, large);
    Equal((byte)70, small);

    report[1] = 0x02; // Yalnız lightbar-valid; rumble state'i değiştirme.
    Assert(!VirtualGamepadService.TryParseDs4RumbleFeedback(
        report,
        out _,
        out _));
}

static void TestDs4TouchPointPacking()
{
    byte[] point = new byte[4];

    // Aktif touch: X=1234 (0x04D2), Y=456 (0x01C8), Id=7.
    VirtualGamepadService.WriteDs4TouchPoint(
        point, active: true, trackingId: 7, x: 1234, y: 456);
    Equal((byte)0x07, point[0]); // contact: active (bit7=0), id 7.
    Equal((byte)0xD2, point[1]); // x_lo.
    Equal((byte)0x84, point[2]); // x_hi=4 | y_lo=8.
    Equal((byte)0x1C, point[3]); // y_hi.

    // Inactive (finger-up): bit7 set, id korunur.
    VirtualGamepadService.WriteDs4TouchPoint(
        point, active: false, trackingId: 7, x: 1234, y: 456);
    Equal((byte)0x87, point[0]);

    // Koordinat clamp: üst sınırlara sıkıştır.
    VirtualGamepadService.WriteDs4TouchPoint(
        point, active: true, trackingId: 0, x: 99999, y: -5);
    Equal((byte)0x00, point[0]);
    Equal((byte)0x7F, point[1]); // x=1919 -> 0x07FF (x_lo=0x7F).
    Equal((byte)0x07, point[2]); // x_hi=7 | y_lo=0.
    Equal((byte)0x00, point[3]); // y=0.

    // Tracking id üst 7 bit ile maskelenir.
    VirtualGamepadService.WriteDs4TouchPoint(
        point, active: true, trackingId: 0x1FF, x: 0, y: 0);
    Equal((byte)0x7F, point[0]);
}

static void TestDs4SecondTouchContactInactive()
{
    var service = new VirtualGamepadService();

    var active = new GamepadOutputState
    {
        Touch1Active = true,
        Touch1X = 600,
        Touch1Y = 300,
        Touch1TrackingId = 9
    };

    byte[] report = service.BuildDs4ReportEx(active);

    // Contact 1 aktif, contact 2 inaktif.
    Equal((byte)1, report[32]);
    Equal((byte)0x00, (byte)(report[34] & 0x80));
    Assert((report[38] & 0x80) != 0);
    Equal((byte)0, report[39]);
    Equal((byte)0, report[40]);
    Equal((byte)0, report[41]);

    // Finger-up frame: contact 1 ve contact 2 inaktif.
    var released = new GamepadOutputState
    {
        Touch1Active = false
    };

    report = service.BuildDs4ReportEx(released);
    Equal((byte)1, report[32]);
    Assert((report[34] & 0x80) != 0);
    Assert((report[38] & 0x80) != 0);

    // Sonraki frame neutral: num_touch_reports = 0.
    report = service.BuildDs4ReportEx(released);
    Equal((byte)0, report[32]);
}

static void TestDs4DualTouchReport()
{
    var service = new VirtualGamepadService();

    var dual = new GamepadOutputState
    {
        Touch1Active = true,
        Touch1X = 600,
        Touch1Y = 300,
        Touch1TrackingId = 9,
        Touch2Active = true,
        Touch2X = 900,
        Touch2Y = 420,
        Touch2TrackingId = 5
    };

    byte[] report = service.BuildDs4ReportEx(dual);

    // Tek touch packet (num_touch_reports=1), iki aktif contact.
    Equal((byte)1, report[32]);

    Equal((byte)0x00, (byte)(report[34] & 0x80)); // contact1 active.
    Equal((byte)0x09, report[34]);                // id 9.
    Equal((byte)0x00, (byte)(report[38] & 0x80)); // contact2 active.
    Equal((byte)0x05, report[38]);                // id 5.

    // Contact2 X/Y packing: X=900 (0x0384), Y=420 (0x01A4).
    Equal((byte)0x84, report[39]); // x_lo.
    Equal((byte)0x43, report[40]); // x_hi=3 | y_lo=4.
    Equal((byte)0x1A, report[41]); // y_hi.

    // Touch2 bağımsız release: Touch1 aktif kalır, Touch2 inaktif.
    var touch2Up = new GamepadOutputState
    {
        Touch1Active = true,
        Touch1X = 700,
        Touch1Y = 350,
        Touch1TrackingId = 9,
        Touch2Active = false
    };

    report = service.BuildDs4ReportEx(touch2Up);
    Equal((byte)1, report[32]);
    Equal((byte)0x00, (byte)(report[34] & 0x80)); // contact1 hâlâ active.
    Assert((report[38] & 0x80) != 0);             // contact2 release.
    Equal((byte)(0x80 | 5), report[38]);          // son id korunur.
}

static void TestTouchOutputCarry()
{
    var emulation = new GamepadEmulationService(
        new GamepadService(),
        new VirtualGamepadService(),
        new ButtonRemapService(),
        new Profile { Name = "T" });

    var input = new PhysicalGamepadState
    {
        IsConnected = true,
        LeftStickX = 128,
        LeftStickY = 128,
        RightStickX = 128,
        RightStickY = 128,
        Touch1Active = true,
        Touch1X = 1234,
        Touch1Y = 456,
        Touch1TrackingId = 17,
        Touch2Active = true,
        Touch2X = 900,
        Touch2Y = 420,
        Touch2TrackingId = 5
    };

    GamepadOutputState output = emulation.BuildOutputState(input);

    Assert(output.Touch1Active);
    Equal(1234, output.Touch1X);
    Equal(456, output.Touch1Y);
    Equal(17, output.Touch1TrackingId);
    Assert(output.Touch2Active);
    Equal(900, output.Touch2X);
    Equal(420, output.Touch2Y);
    Equal(5, output.Touch2TrackingId);

    // Inactive durum temiz taşınır.
    input.Touch1Active = false;
    input.Touch2Active = false;
    output = emulation.BuildOutputState(input);
    Assert(!output.Touch1Active);
    Assert(!output.Touch2Active);
}

static void TestTouchpadModeRoundTrip()
{
    // Eski profil (TouchpadMode alanı yok) → default Normal.
    var legacy = JsonSerializer.Deserialize<ControllerProfileSettings>("{}");
    Assert(legacy != null);
    Equal(TouchpadMode.Normal, legacy!.TouchpadMode);

    var settings = new ControllerProfileSettings
    {
        TouchpadMode = TouchpadMode.Mouse
    };

    string json = JsonSerializer.Serialize(settings);
    var roundTripped =
        JsonSerializer.Deserialize<ControllerProfileSettings>(json);
    Assert(roundTripped != null);
    Equal(TouchpadMode.Mouse, roundTripped!.TouchpadMode);

    ControllerProfileSettings clone = settings.Clone();
    Equal(TouchpadMode.Mouse, clone.TouchpadMode);
}

static void TestTouchpadModeOutputFilter()
{
    var input = new PhysicalGamepadState
    {
        IsConnected = true,
        LeftStickX = 128,
        LeftStickY = 128,
        RightStickX = 128,
        RightStickY = 128,
        Touch1Active = true,
        Touch1X = 100,
        Touch1Y = 200,
        Touch1TrackingId = 3,
        Touch2Active = true,
        Touch2X = 300,
        Touch2Y = 400,
        Touch2TrackingId = 4,
        TouchpadPressed = true
    };

    GamepadOutputState normal = BuildOutputForMode(TouchpadMode.Normal, input);
    Assert(normal.Touch1Active && normal.Touch2Active);
    Assert(normal.TouchpadPressed);

    GamepadOutputState mouse = BuildOutputForMode(TouchpadMode.Mouse, input);
    Assert(!mouse.Touch1Active && !mouse.Touch2Active);
    Assert(!mouse.TouchpadPressed);

    GamepadOutputState disabled =
        BuildOutputForMode(TouchpadMode.Disabled, input);
    Assert(!disabled.Touch1Active && !disabled.Touch2Active);
    Assert(!disabled.TouchpadPressed);
}

static GamepadOutputState BuildOutputForMode(
    TouchpadMode mode,
    PhysicalGamepadState input)
{
    var profile = new Profile { Name = "M" };
    profile.ControllerSettings.TouchpadMode = mode;

    var emulation = new GamepadEmulationService(
        new GamepadService(),
        new VirtualGamepadService(),
        new ButtonRemapService(),
        profile);

    return emulation.BuildOutputState(input);
}

static void TestProfileAtomicRecovery()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        "aRacnid-profile-test-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(directory, "profiles.json");
    Directory.CreateDirectory(directory);

    try
    {
        var service = new ProfileService(path);
        service.SaveProfiles([
            new Profile { Name = "Backup Profile", IsMainProfile = true }
        ]);
        service.SaveProfiles([
            new Profile { Name = "Current Profile", IsMainProfile = true }
        ]);

        File.WriteAllText(path, "{broken json");

        List<Profile> recovered = service.LoadProfiles();
        Equal("Backup Profile", recovered.Single().Name);
        Assert(File.Exists(path));
        Assert(Directory.GetFiles(directory, "*.corrupt-*.json").Length == 1);
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

static void TestSettingsAtomicRecovery()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        "aRacnid-settings-test-" + Guid.NewGuid().ToString("N"));

    string path = Path.Combine(
        directory,
        "settings.json");

    Directory.CreateDirectory(directory);

    try
    {
        var service = new SettingsService(path);

        service.SaveSettings(new AppSettings
        {
            Language = "EN",
            LastProfileName = "Backup Profile",
            RunAtStartup = true
        });

        service.SaveSettings(new AppSettings
        {
            Language = "TR",
            LastProfileName = "Current Profile",
            RunAtStartup = false
        });

        File.WriteAllText(
            path,
            "{broken json");

        AppSettings recovered =
            service.LoadSettings();

        Equal("EN", recovered.Language);
        Equal("Backup Profile", recovered.LastProfileName);
        Equal(true, recovered.RunAtStartup);

        Assert(File.Exists(path));

        Assert(
            Directory.GetFiles(
                directory,
                "settings.corrupt-*.json").Length == 1);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }
}

static void TestLocalizationJson()
{
    foreach (string language in new[] { "tr", "en" })
    {
        string json = File.ReadAllText(
            ProjectFile("Localization", language + ".json"));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert(document.RootElement.ValueKind == JsonValueKind.Object);
        Assert(document.RootElement.TryGetProperty(
            "controls.press_button", out _));
        Assert(document.RootElement.TryGetProperty(
            "profile.delete_confirm", out _));
    }
}

static void TestSingleSubmitSourceGuard()
{
    string source = File.ReadAllText(ProjectFile("services", "VirtualGamepadService.cs"));

    Equal(2, Regex.Matches(source, @"AutoSubmitReport\s*=\s*false").Count);
    Equal(0, Regex.Matches(source, @"AutoSubmitReport\s*=\s*true").Count);
    Equal(1, Regex.Matches(source, @"\.SubmitReport\s*\(\s*\)\s*;").Count);
    Equal(1, Regex.Matches(source, @"\.SubmitRawReport\s*\(\s*report\s*\)\s*;").Count);
    Equal(1, Regex.Matches(
        source,
        @"DualShock4Button\.TriggerLeft\.Value").Count);
    Equal(1, Regex.Matches(
        source,
        @"DualShock4Button\.TriggerRight\.Value").Count);
}

static void TestSinglePhysicalReaderSourceGuard()
{
    string main = File.ReadAllText(ProjectFile("MainWindow.xaml.cs"));
    string bindings = File.ReadAllText(
        ProjectFile("views", "KeyBindingsView.xaml.cs"));

    foreach (string source in new[] { main, bindings })
    {
        Assert(!source.Contains("HidDevice", StringComparison.Ordinal));
        Assert(!source.Contains("ReadInputReport", StringComparison.Ordinal));
        Assert(!source.Contains("FindGamepad", StringComparison.Ordinal));
    }
}

static void TestSdlNativeAbi()
{
    string dllPath = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
    Assert(File.Exists(dllPath), "SDL3.dll was not copied beside the test executable.");

    nint library = NativeLibrary.Load(dllPath);

    try
    {
        string[] requiredExports =
        [
            "SDL_SetHint", "SDL_SetHintWithPriority", "SDL_InitSubSystem",
            "SDL_QuitSubSystem", "SDL_SetGamepadEventsEnabled",
            "SDL_GetGamepads", "SDL_OpenGamepad", "SDL_GetGamepadNameForID",
            "SDL_GetGamepadPathForID", "SDL_GetGamepadVendorForID",
            "SDL_GetGamepadProductForID", "SDL_GetGamepadTypeForID",
            "SDL_GetGamepadConnectionState", "SDL_GetGamepadPowerInfo",
            "SDL_GamepadConnected", "SDL_GetGamepadAxis",
            "SDL_GetGamepadButton", "SDL_UpdateGamepads",
            "SDL_RumbleGamepad", "SDL_SetGamepadLED", "SDL_CloseGamepad",
            "SDL_free", "SDL_GetError", "SDL_GetVersion"
        ];

        foreach (string name in requiredExports)
        {
            Assert(
                NativeLibrary.TryGetExport(library, name, out _),
                $"Missing native export: {name}");
        }

        nint versionAddress = NativeLibrary.GetExport(library, "SDL_GetVersion");
        var getVersion = Marshal.GetDelegateForFunctionPointer<SdlGetVersion>(
            versionAddress);
        Equal(3_004_014, getVersion());

        nint initAddress = NativeLibrary.GetExport(library, "SDL_InitSubSystem");
        nint quitAddress = NativeLibrary.GetExport(library, "SDL_QuitSubSystem");
        var init = Marshal.GetDelegateForFunctionPointer<SdlInitSubSystem>(
            initAddress);
        var quit = Marshal.GetDelegateForFunctionPointer<SdlQuitSubSystem>(
            quitAddress);

        Assert(init(0x00002000));
        quit(0x00002000);
    }
    finally
    {
        NativeLibrary.Free(library);
    }
}

static void TestLiveVigemFilter()
{
    using var client = new ViGEmClient();
    var ds4 = client.CreateDualShock4Controller();
    var xbox = client.CreateXbox360Controller();

    try
    {
        ds4.Connect();
        xbox.Connect();

        DateTime deadline =
            DateTime.UtcNow.AddSeconds(2);

        bool ds4Ready = false;
        bool xboxReady = false;

        while (DateTime.UtcNow < deadline)
        {
            HidDevice[] virtualDs4 = DeviceList.Local
                .GetHidDevices(0x054C, 0x05C4)
                .ToArray();

            HidDevice[] virtualXbox = DeviceList.Local
                .GetHidDevices(0x045E, 0x028E)
                .ToArray();

            ds4Ready =
                virtualDs4.Any(PhysicalDeviceFilter.IsVirtual);

            xboxReady =
                virtualXbox.Any(PhysicalDeviceFilter.IsVirtual);

            if (ds4Ready && xboxReady)
                break;

            Thread.Sleep(100);
        }

        Assert(
            ds4Ready,
            "The test ViGEm DS4 did not pass through the virtual filter.");

        Assert(
            xboxReady,
            "The test ViGEm Xbox did not pass through the virtual filter.");
    }
    finally
    {
        try { xbox.Disconnect(); } catch { }
        try { ds4.Disconnect(); } catch { }
        (xbox as IDisposable)?.Dispose();
        ds4.Dispose();
    }

    // HidHide bu test çalıştırılabilir dosyasını whitelist'e almadığında
    // fiziksel DS4'ün burada görünmemesi beklenir. Görünürse yine de ters
    // sınıflandırılmadığını doğrularız. Fiziksel canlı akış ayrı RawReader
    // testiyle, whitelist'teki iki süreç üzerinden sınanır.
    HidDevice[] physical = DeviceList.Local
        .GetHidDevices(0x054C, 0x09CC)
        .ToArray();

    if (physical.Length > 0)
    {
        Assert(
            physical.Any(device => !PhysicalDeviceFilter.IsVirtual(device)),
            "The physical DS4 v2 was classified as virtual.");
    }
}

static byte[] CreateUsbReport()
{
    byte[] report = new byte[64];
    report[0] = 0x01;
    report[1] = 128;
    report[2] = 128;
    report[3] = 128;
    report[4] = 128;
    report[5] = 0x08; // D-pad neutral.
    report[30] = 5;
    return report;
}

static string ProjectFile(params string[] parts)
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);

    while (directory != null &&
           !File.Exists(Path.Combine(directory.FullName, "GamepadApp.csproj")))
    {
        directory = directory.Parent;
    }

    Assert(directory != null, "Project root could not be located.");
    return Path.Combine([directory!.FullName, .. parts]);
}

static void SetEqual(IEnumerable<string> expected, IEnumerable<string> actual)
{
    var expectedSet = new HashSet<string>(expected);
    var actualSet = new HashSet<string>(actual);
    Assert(
        expectedSet.SetEquals(actualSet),
        $"Expected [{string.Join(", ", expectedSet)}], " +
        $"actual [{string.Join(", ", actualSet)}].");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
}

static void Assert(bool condition, string message = "Assertion failed.")
{
    if (!condition)
        throw new InvalidOperationException(message);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int SdlGetVersion();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool SdlInitSubSystem(uint flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void SdlQuitSubSystem(uint flags);
