using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace GamepadApp.Services;

internal enum HidDeviceOrigin
{
    Physical,
    ViGEmVirtual,
    Unknown
}

internal static class WindowsHidDeviceInspector
{
    private const int ErrorInsufficientBuffer = 122;
    private const uint CrSuccess = 0x00000000;
    private const uint CrBufferSmall = 0x0000001A;

    private static readonly nint InvalidHandleValue = new(-1);
    private static readonly Guid DevicePropertyCategory =
        new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Devpropkey DeviceHardwareIds =
        new(DevicePropertyCategory, 3);
    private static readonly Devpropkey DeviceService =
        new(DevicePropertyCategory, 6);

    private static readonly ConcurrentDictionary<string, HidDeviceOrigin>
        OriginCache = new(StringComparer.OrdinalIgnoreCase);

    public static HidDeviceOrigin ResolveOrigin(string? devicePath)
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(devicePath))
        {
            return HidDeviceOrigin.Unknown;
        }

        if (OriginCache.TryGetValue(devicePath, out HidDeviceOrigin cached))
            return cached;

        HidDeviceOrigin result = InspectPath(devicePath);

        // Geçici PnP hatası sonraki taramada yeniden denensin.
        if (result != HidDeviceOrigin.Unknown)
            OriginCache[devicePath] = result;

        return result;
    }

    private static HidDeviceOrigin InspectPath(string devicePath)
    {
        nint deviceInfoSet = SetupDiCreateDeviceInfoList(
            nint.Zero,
            nint.Zero);

        if (deviceInfoSet == InvalidHandleValue)
            return HidDeviceOrigin.Unknown;

        var interfaceData = new SpDeviceInterfaceData
        {
            Size = checked(
                (uint)Marshal.SizeOf<SpDeviceInterfaceData>())
        };
        bool interfaceOpened = false;

        try
        {
            if (!SetupDiOpenDeviceInterfaceW(
                    deviceInfoSet,
                    devicePath,
                    0,
                    ref interfaceData))
            {
                return HidDeviceOrigin.Unknown;
            }

            interfaceOpened = true;

            var deviceInfoData = new SpDevinfoData
            {
                Size = checked((uint)Marshal.SizeOf<SpDevinfoData>())
            };

            bool detailResult = SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet,
                ref interfaceData,
                nint.Zero,
                0,
                out _,
                ref deviceInfoData);

            int detailError = Marshal.GetLastWin32Error();

            if (!detailResult && detailError != ErrorInsufficientBuffer)
                return HidDeviceOrigin.Unknown;

            return InspectParentChain(deviceInfoData.DevInst);
        }
        catch
        {
            return HidDeviceOrigin.Unknown;
        }
        finally
        {
            if (interfaceOpened)
            {
                SetupDiDeleteDeviceInterfaceData(
                    deviceInfoSet,
                    ref interfaceData);
            }

            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static HidDeviceOrigin InspectParentChain(uint firstDevInst)
    {
        var visited = new HashSet<uint>();
        uint current = firstDevInst;
        bool physicalBusSeen = false;

        for (int depth = 0; depth < 64 && visited.Add(current); depth++)
        {
            string instanceId = GetDeviceInstanceId(current);
            string service = GetStringProperty(current, DeviceService);
            string[] hardwareIds =
                GetStringListProperty(current, DeviceHardwareIds);

            if (IsViGEmMarker(instanceId, service, hardwareIds))
                return HidDeviceOrigin.ViGEmVirtual;

            if (IsPhysicalBus(instanceId))
                physicalBusSeen = true;

            if (CM_Get_Parent(out uint parent, current, 0) != CrSuccess)
                break;

            current = parent;
        }

        // ViGEm çocukları da alt katmanda USB\VID_... düğümü taşıyabilir.
        // Bu nedenle fiziksel işaretini ancak bütün üst zincir ViGEm için
        // tarandıktan sonra kesinleştiririz.
        return physicalBusSeen
            ? HidDeviceOrigin.Physical
            : HidDeviceOrigin.Unknown;
    }

    private static bool IsViGEmMarker(
        string instanceId,
        string service,
        IEnumerable<string> hardwareIds)
    {
        if (service.Equals("ViGEmBus", StringComparison.OrdinalIgnoreCase))
            return true;

        if (instanceId.StartsWith(
                "ROOT\\VIGEMBUS\\",
                StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(
                "NEFARIUS\\VIGEMBUS\\",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return hardwareIds.Any(id =>
            id.Equals(
                "Nefarius\\ViGEmBus\\Gen1",
                StringComparison.OrdinalIgnoreCase) ||
            id.Equals(
                "Root\\ViGEmBus",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPhysicalBus(string instanceId)
    {
        return instanceId.StartsWith(
                   "USB\\",
                   StringComparison.OrdinalIgnoreCase) ||
               instanceId.StartsWith(
                   "BTHENUM\\",
                   StringComparison.OrdinalIgnoreCase) ||
               instanceId.StartsWith(
                   "BTHLEDEVICE\\",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDeviceInstanceId(uint devInst)
    {
        if (CM_Get_Device_ID_Size(out uint length, devInst, 0) != CrSuccess)
            return string.Empty;

        var buffer = new StringBuilder(checked((int)length + 1));

        return CM_Get_Device_IDW(
                   devInst,
                   buffer,
                   length + 1,
                   0) == CrSuccess
            ? buffer.ToString()
            : string.Empty;
    }

    private static string GetStringProperty(
        uint devInst,
        Devpropkey propertyKey)
    {
        string[] values = GetPropertyStrings(devInst, propertyKey);
        return values.FirstOrDefault() ?? string.Empty;
    }

    private static string[] GetStringListProperty(
        uint devInst,
        Devpropkey propertyKey)
    {
        return GetPropertyStrings(devInst, propertyKey);
    }

    private static string[] GetPropertyStrings(
        uint devInst,
        Devpropkey propertyKey)
    {
        uint size = 0;
        Devpropkey key = propertyKey;
        uint result = CM_Get_DevNode_PropertyW(
            devInst,
            ref key,
            out _,
            nint.Zero,
            ref size,
            0);

        if (result != CrBufferSmall || size < sizeof(char))
            return [];

        nint buffer = Marshal.AllocHGlobal(checked((int)size));

        try
        {
            result = CM_Get_DevNode_PropertyW(
                devInst,
                ref key,
                out _,
                buffer,
                ref size,
                0);

            if (result != CrSuccess)
                return [];

            string raw = Marshal.PtrToStringUni(
                buffer,
                checked((int)size / sizeof(char))) ?? string.Empty;

            return raw
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey
    {
        public Guid FormatId;
        public uint PropertyId;

        public Devpropkey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetupDiCreateDeviceInfoList(
        nint classGuid,
        nint parentWindow);

    [DllImport(
        "setupapi.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInterfaceW(
        nint deviceInfoSet,
        string devicePath,
        uint openFlags,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport(
        "setupapi.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        nint deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDeleteDeviceInterfaceData(
        nint deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(
        nint deviceInfoSet);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(
        out uint parentDevInst,
        uint devInst,
        uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_Size(
        out uint length,
        uint devInst,
        uint flags);

    [DllImport(
        "cfgmgr32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(
        uint devInst,
        StringBuilder buffer,
        uint bufferLength,
        uint flags);

    [DllImport(
        "cfgmgr32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(
        uint devInst,
        ref Devpropkey propertyKey,
        out uint propertyType,
        nint propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);
}
