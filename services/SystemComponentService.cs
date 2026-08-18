using System.Diagnostics;
using System.IO;
using Nefarius.ViGEm.Client;

namespace GamepadApp.Services;

public static class SystemComponentService
{
    public static bool IsViGEmBusInstalled()
    {
        try
        {
            using var testClient = new ViGEmClient();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsHidHideInstalled()
    {
        // HidHide kaldırıldıktan sonra sürücü servis anahtarı Windows'ta
        // kalabiliyor. Yapılandırma istemcisi yoksa bileşen kullanılamaz;
        // bu nedenle gerçek kurulum dosyasını esas alıyoruz.
        return FindHidHideClientPath() != null;
    }

    public static bool TryOpenHidHideConfiguration()
    {
        string? clientPath = FindHidHideClientPath();

        if (clientPath == null)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = clientPath,
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindHidHideClientPath()
    {
        string programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);

        string[] possiblePaths =
        [
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions",
                "HidHide",
                "HidHideClient.exe"),
            Path.Combine(
                programFiles,
                "Nefarius Software Solutions",
                "HidHide",
                "x64",
                "HidHideClient.exe"),
            Path.Combine(
                programFiles,
                "HidHide",
                "x64",
                "HidHideClient.exe")
        ];

        return possiblePaths.FirstOrDefault(File.Exists);
    }
}
