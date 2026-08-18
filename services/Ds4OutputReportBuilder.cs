using System.Buffers.Binary;

namespace GamepadApp.Services;

public readonly record struct Ds4OutputState(
    byte LeftMotor,
    byte RightMotor,
    byte Red,
    byte Green,
    byte Blue,
    bool HasRumble,
    bool HasLightbar);

public static class Ds4OutputReportBuilder
{
    private const byte RumbleValid = 0x01;
    private const byte LightbarValid = 0x02;

    public static byte[] BuildUsb(Ds4OutputState state)
    {
        byte[] report = new byte[32];
        report[0] = 0x05;
        report[1] = GetValidMask(state);
        report[4] = state.RightMotor;
        report[5] = state.LeftMotor;
        report[6] = state.Red;
        report[7] = state.Green;
        report[8] = state.Blue;
        return report;
    }

    public static byte[] BuildBluetooth(Ds4OutputState state)
    {
        byte[] report = new byte[78];
        report[0] = 0x11;
        report[1] = 0xC4; // HID + CRC, 4 ms report interval.
        report[3] = GetValidMask(state);
        report[6] = state.RightMotor;
        report[7] = state.LeftMotor;
        report[8] = state.Red;
        report[9] = state.Green;
        report[10] = state.Blue;
        FinalizeBluetoothCrc(report, 0xA2);
        return report;
    }

    public static bool IsBluetoothCrcValid(
        ReadOnlySpan<byte> report,
        byte hidHeader)
    {
        if (report.Length < 78)
            return false;

        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(
            report.Slice(74, sizeof(uint)));
        uint actual = ComputeBluetoothCrc(
            report.Slice(0, 74),
            hidHeader);
        return expected == actual;
    }

    public static void FinalizeBluetoothCrc(
        Span<byte> report,
        byte hidHeader)
    {
        if (report.Length < 78)
            throw new ArgumentException(
                "DS4 Bluetooth raporu en az 78 bayt olmalıdır.",
                nameof(report));

        uint crc = ComputeBluetoothCrc(
            report.Slice(0, 74),
            hidHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(
            report.Slice(74, sizeof(uint)),
            crc);
    }

    private static byte GetValidMask(Ds4OutputState state)
    {
        byte mask = 0;

        if (state.HasRumble)
            mask |= RumbleValid;

        if (state.HasLightbar)
            mask |= LightbarValid;

        return mask;
    }

    private static uint ComputeBluetoothCrc(
        ReadOnlySpan<byte> reportPrefix,
        byte hidHeader)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc32(crc, hidHeader);

        foreach (byte value in reportPrefix)
            crc = UpdateCrc32(crc, value);

        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;

        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0
                ? (crc >> 1) ^ 0xEDB88320
                : crc >> 1;
        }

        return crc;
    }
}
