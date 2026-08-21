using System.Diagnostics;
using HidSharp;

namespace GamepadApp.Services;

internal sealed class DualShock4HidProvider : IPhysicalGamepadProvider
{
    private const int SonyVendorId = 0x054C;

    private static readonly IReadOnlyDictionary<int, string> ProductNames =
        new Dictionary<int, string>
        {
            [0x05C4] = "DualShock 4 v1",
            [0x09CC] = "DualShock 4 v2",
            [0x0BA0] = "DualShock 4 Wireless Adapter"
        };

    public IPhysicalGamepadSession? TryOpen()
    {
        foreach (HidDevice device in FindSupportedDevices())
        {
            try
            {
                var session = new DualShock4HidSession(device);

                if (!session.TryPrimeWirelessAdapter())
                {
                    session.Dispose();
                    continue;
                }

                return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"DS4 açılamadı ({device.DevicePath}): {ex.Message}");
            }
        }

        return null;
    }

    private static IEnumerable<HidDevice> FindSupportedDevices()
    {
        return DeviceList.Local
            .GetHidDevices(SonyVendorId)
            .Where(device => ProductNames.ContainsKey(device.ProductID))
            .Where(device => !PhysicalDeviceFilter.IsVirtual(device))
            .OrderBy(device => device.ProductID == 0x09CC ? 0 : 1)
            .ThenBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
    }

    private sealed class DualShock4HidSession : IPhysicalGamepadSession
    {
        private const byte Ds4FeatureReportCalibrationId = 0x02;
        private const int Ds4FeatureReportCalibrationLength = 37;

        private readonly HidStream stream;
        private readonly byte[] readBuffer;
        private readonly bool bluetooth;
        private readonly bool wirelessAdapter;
        private readonly object outputSync = new();
        private readonly ManualResetEventSlim outputFlushed = new(true);
        private Ds4OutputState outputState;
        private long pendingOutputVersion;
        private long flushedOutputVersion;
        private long sequenceNumber;
        private long lastValidFrameTicks = Stopwatch.GetTimestamp();
        private long lastTickleTicks;
        private long rumbleStopDeadlineTicks;
        private PhysicalGamepadState? primedState;
        private bool enhancedReportDetected;
        private volatile bool disposed;

        public DualShock4HidSession(HidDevice device)
        {
            bluetooth = device.GetMaxInputReportLength() > 64;
            wirelessAdapter = device.ProductID == 0x0BA0;

            PhysicalConnectionType connectionType =
                device.ProductID == 0x0BA0
                    ? PhysicalConnectionType.WirelessReceiver
                    : bluetooth
                        ? PhysicalConnectionType.Bluetooth
                        : PhysicalConnectionType.USB;

            Descriptor = new PhysicalGamepadDescriptor(
                device.DevicePath,
                ProductNames[device.ProductID],
                PhysicalControllerType.DualShock4,
                connectionType,
                (ushort)device.VendorID,
                (ushort)device.ProductID,
                SupportsRumble: true,
                SupportsLightbar: true);

            stream = device.Open();
            stream.ReadTimeout = 100;
            stream.WriteTimeout = 1000;
            readBuffer = new byte[Math.Max(
                device.GetMaxInputReportLength(),
                bluetooth ? 78 : 64)];

            if (bluetooth)
            {
                // Feature Report 0x02 (kalibrasyon) isteği DS4 firmware'ini
                // Bluetooth tarafında tam 0x11 input report moduna geçirir.
                // Best-effort: başarısız olursa minimal 0x01 akışı devam eder.
                TryEnableBluetoothEnhancedReports(
                    device.GetMaxFeatureReportLength());

                // DS4 Bluetooth başlangıçta yalnız 10 baytlık minimal 0x01
                // raporu gönderebilir. Hiçbir efekt alanını geçerli saymayan
                // bu no-op paket, fiziksel LED/rumble'ı değiştirmeden bağlantıyı
                // canlı tutar.
                stream.Write(
                    Ds4OutputReportBuilder.BuildBluetooth(default));
            }
        }

        private void TryEnableBluetoothEnhancedReports(
            int featureReportLength)
        {
            Debug.WriteLine(
                "DS4 Bluetooth: requesting feature report 0x02");

            try
            {
                int length = Math.Max(
                    featureReportLength,
                    Ds4FeatureReportCalibrationLength);

                byte[] buffer = new byte[length];
                buffer[0] = Ds4FeatureReportCalibrationId;

                stream.GetFeature(buffer, 0, buffer.Length);

                Debug.WriteLine(
                    "DS4 Bluetooth: enhanced report request succeeded");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "DS4 Bluetooth: enhanced report request failed: " +
                    ex.Message);
            }
        }

        public PhysicalGamepadDescriptor Descriptor { get; }

        public bool TryPrimeWirelessAdapter()
        {
            if (!wirelessAdapter)
                return true;

            try
            {
                int bytesRead = stream.Read(readBuffer);

                if (bytesRead < 64)
                    return false;

                ReadOnlySpan<byte> report =
                    readBuffer.AsSpan(0, bytesRead);

                if (Ds4ReportParser.IsWirelessAdapterDisconnected(report))
                    return false;

                if (!Ds4ReportParser.TryParse(
                        report,
                        Ds4InputTransport.Usb,
                        Interlocked.Increment(ref sequenceNumber),
                        out PhysicalGamepadState parsed))
                {
                    return false;
                }

                primedState = parsed;
                lastValidFrameTicks = Stopwatch.GetTimestamp();
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public PhysicalReadResult ReadNext(out PhysicalGamepadState? state)
        {
            state = null;

            if (disposed)
                return PhysicalReadResult.Disconnected;

            PhysicalGamepadState? initialState = primedState;

            if (initialState != null)
            {
                primedState = null;
                state = initialState;
                return PhysicalReadResult.State;
            }

            try
            {
                ApplyRumbleDeadline();
                FlushOutputCommands();

                int bytesRead = stream.Read(readBuffer);

                if (bytesRead <= 0)
                    return PhysicalReadResult.Disconnected;

                ReadOnlySpan<byte> report =
                    readBuffer.AsSpan(0, bytesRead);

                if (wirelessAdapter &&
                    Ds4ReportParser.IsWirelessAdapterDisconnected(report))
                {
                    return PhysicalReadResult.Disconnected;
                }

                if (!Ds4ReportParser.TryParse(
                        report,
                        bluetooth
                            ? Ds4InputTransport.Bluetooth
                            : Ds4InputTransport.Usb,
                        Interlocked.Increment(ref sequenceNumber),
                        out PhysicalGamepadState parsed))
                {
                    return HandleInputSilence();
                }

                if (bluetooth && !enhancedReportDetected &&
                    report[0] == 0x11)
                {
                    enhancedReportDetected = true;
                    Debug.WriteLine(
                        "DS4 Bluetooth: enhanced input report 0x11 detected");
                }

                lastValidFrameTicks = Stopwatch.GetTimestamp();
                state = parsed;
                return PhysicalReadResult.State;
            }
            catch (TimeoutException)
            {
                return HandleInputSilence();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DS4 okuma bağlantısı kesildi: {ex.Message}");
                return PhysicalReadResult.Disconnected;
            }
        }

        public bool TrySetVibration(
            byte leftMotor,
            byte rightMotor,
            uint durationMs)
        {
            if (disposed)
                return false;

            lock (outputSync)
            {
                if (disposed)
                    return false;

                outputState = outputState with
                {
                    LeftMotor = leftMotor,
                    RightMotor = rightMotor,
                    HasRumble = true
                };
                rumbleStopDeadlineTicks = durationMs > 0 &&
                                         (leftMotor != 0 || rightMotor != 0)
                    ? Stopwatch.GetTimestamp() +
                      (long)(durationMs * (double)Stopwatch.Frequency / 1000.0)
                    : 0;
                pendingOutputVersion++;
                outputFlushed.Reset();
            }

            return true;
        }

        public bool TrySetLightbar(byte red, byte green, byte blue)
        {
            if (disposed)
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
                outputFlushed.Reset();
            }

            return true;
        }

        private void FlushOutputCommands()
        {
            Ds4OutputState snapshot;
            long version;

            lock (outputSync)
            {
                if (pendingOutputVersion == flushedOutputVersion)
                    return;

                snapshot = outputState;
                version = pendingOutputVersion;
            }

            byte[] report = bluetooth
                ? Ds4OutputReportBuilder.BuildBluetooth(snapshot)
                : Ds4OutputReportBuilder.BuildUsb(snapshot);
            stream.Write(report);

            if (bluetooth)
                lastTickleTicks = Stopwatch.GetTimestamp();

            lock (outputSync)
            {
                flushedOutputVersion = Math.Max(
                    flushedOutputVersion,
                    version);

                if (flushedOutputVersion == pendingOutputVersion)
                    outputFlushed.Set();
            }
        }

        public bool WaitForPendingOutput(int timeoutMs)
        {
            if (disposed)
                return true;

            return outputFlushed.Wait(Math.Max(0, timeoutMs));
        }

        private void ApplyRumbleDeadline()
        {
            long deadline = rumbleStopDeadlineTicks;

            if (deadline == 0 || Stopwatch.GetTimestamp() < deadline)
                return;

            lock (outputSync)
            {
                if (rumbleStopDeadlineTicks == 0 ||
                    Stopwatch.GetTimestamp() < rumbleStopDeadlineTicks)
                {
                    return;
                }

                rumbleStopDeadlineTicks = 0;

                if (!outputState.HasRumble ||
                    (outputState.LeftMotor == 0 &&
                     outputState.RightMotor == 0))
                {
                    return;
                }

                outputState = outputState with
                {
                    LeftMotor = 0,
                    RightMotor = 0
                };
                pendingOutputVersion++;
                outputFlushed.Reset();
            }
        }

        private PhysicalReadResult HandleInputSilence()
        {
            long now = Stopwatch.GetTimestamp();

            if (bluetooth &&
                ElapsedMilliseconds(lastTickleTicks, now) >= 500)
            {
                try
                {
                    // Açık kalan ama rapor üretmeyen BT handle'ını yokla.
                    // Mask=0 fiziksel efekt state'ini değiştirmez.
                    stream.Write(
                        Ds4OutputReportBuilder.BuildBluetooth(default));
                    lastTickleTicks = now;
                }
                catch
                {
                    return PhysicalReadResult.Disconnected;
                }
            }

            return ElapsedMilliseconds(lastValidFrameTicks, now) >= 2000
                ? PhysicalReadResult.Disconnected
                : PhysicalReadResult.Timeout;
        }

        private static double ElapsedMilliseconds(long start, long end)
        {
            if (start == 0)
                return double.MaxValue;

            return (end - start) * 1000.0 / Stopwatch.Frequency;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            outputFlushed.Set();

            try
            {
                stream.Dispose();
            }
            catch
            {
            }
        }
    }

}
