using System.Runtime.InteropServices;
using System.IO;

namespace GamepadApp.Services;

internal static class Sdl3Native
{
    internal const uint InitGamepad = 0x00002000;

    private const string LibraryName = "SDL3.dll";

    static Sdl3Native()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(Sdl3Native).Assembly,
            (libraryName, _, _) =>
            {
                if (!libraryName.Equals(
                        LibraryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return nint.Zero;
                }

                string fixedPath = Path.Combine(
                    AppContext.BaseDirectory,
                    LibraryName);

                // PATH/current-directory üzerinden farklı bir SDL sürümü
                // yüklenmesine izin verme. Paketlenmiş dosya yoksa mutlak
                // yol yüklemesi kontrollü biçimde başarısız olur.
                return NativeLibrary.Load(fixedPath);
            });
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_SetHintWithPriority(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        int priority);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_SetGamepadEventsEnabled(byte enabled);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint SDL_GetGamepads(out int count);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint SDL_OpenGamepad(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint SDL_GetGamepadNameForID(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint SDL_GetGamepadPathForID(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ushort SDL_GetGamepadVendorForID(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ushort SDL_GetGamepadProductForID(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetGamepadTypeForID(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetGamepadConnectionState(nint gamepad);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetGamepadPowerInfo(
        nint gamepad,
        out int percent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GamepadConnected(nint gamepad);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern short SDL_GetGamepadAxis(nint gamepad, int axis);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GetGamepadButton(nint gamepad, int button);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_UpdateGamepads();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_RumbleGamepad(
        nint gamepad,
        ushort lowFrequency,
        ushort highFrequency,
        uint durationMs);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_SetGamepadLED(
        nint gamepad,
        byte red,
        byte green,
        byte blue);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_CloseGamepad(nint gamepad);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_free(nint memory);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint SDL_GetError();

    internal static string GetError()
    {
        nint error = SDL_GetError();
        return error == nint.Zero
            ? "Bilinmeyen SDL hatası"
            : Marshal.PtrToStringUTF8(error) ?? "Bilinmeyen SDL hatası";
    }

    internal static string GetUtf8String(nint pointer)
    {
        return pointer == nint.Zero
            ? string.Empty
            : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}
