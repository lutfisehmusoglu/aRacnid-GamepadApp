using System.IO;
using System.Text;
using System.Text.Json;

namespace GamepadApp.Services;

public class AppSettings
{
    public bool RunAtStartup { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowConnectionNotifications { get; set; } = true;
    public string Language { get; set; } = "TR";
    public string LastProfileName { get; set; } = "";
}

public class SettingsService
{
    private readonly string filePath;
    private readonly string backupPath;

    private const string StartupRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "GamepadApp";

    public SettingsService()
        : this(GetDefaultFilePath())
    {
    }

    public SettingsService(string storageFilePath)
    {
        if (string.IsNullOrWhiteSpace(storageFilePath))
        {
            throw new ArgumentException(
                "Ayar dosyası yolu boş olamaz.",
                nameof(storageFilePath));
        }

        filePath = Path.GetFullPath(storageFilePath);
        backupPath = filePath + ".bak";

        string? folderPath = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    private static string GetDefaultFilePath()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        return Path.Combine(
            appData,
            "GamepadApp",
            "settings.json");
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(filePath))
        {
            if (TryReadSettings(
                    backupPath,
                    out AppSettings? backupSettings))
            {
                RestorePrimaryFromBackup();
                return backupSettings!;
            }

            var defaults = new AppSettings();
            SaveSettings(defaults);
            return defaults;
        }

        if (TryReadSettings(
                filePath,
                out AppSettings? settings))
        {
            return settings!;
        }

        if (TryReadSettings(
                backupPath,
                out AppSettings? recoveredSettings))
        {
            RestorePrimaryFromBackup();
            return recoveredSettings!;
        }

        QuarantineCorruptPrimary();

        var fallback = new AppSettings();
        SaveSettings(fallback);
        return fallback;
    }

    public void SaveSettings(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        string? directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath =
            filePath + "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(
                           encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                try
                {
                    File.Replace(
                        temporaryPath,
                        filePath,
                        backupPath,
                        ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceWithMoveFallback(temporaryPath);
                }
                catch (IOException)
                {
                    ReplaceWithMoveFallback(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void ReplaceWithMoveFallback(
        string temporaryPath)
    {
        if (File.Exists(filePath))
        {
            File.Copy(
                filePath,
                backupPath,
                overwrite: true);
        }

        File.Move(
            temporaryPath,
            filePath,
            overwrite: true);
    }

    private static bool TryReadSettings(
        string path,
        out AppSettings? settings)
    {
        settings = null;

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);

            settings =
                JsonSerializer.Deserialize<AppSettings>(json);

            return settings != null;
        }
        catch
        {
            return false;
        }
    }

    private void RestorePrimaryFromBackup()
    {
        if (!File.Exists(backupPath))
            return;

        try
        {
            QuarantineCorruptPrimary();

            File.Copy(
                backupPath,
                filePath,
                overwrite: true);
        }
        catch
        {
            // Yedek başarıyla okunduysa disk onarımı başarısız olsa bile
            // bu oturumda ayarlar kaybolmaz.
        }
    }

    private void QuarantineCorruptPrimary()
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            string directory =
                Path.GetDirectoryName(filePath) ?? "";

            string name =
                Path.GetFileNameWithoutExtension(filePath);

            string extension =
                Path.GetExtension(filePath);

            string quarantinePath = Path.Combine(
                directory,
                $"{name}.corrupt-" +
                $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-" +
                $"{Guid.NewGuid():N}{extension}");

            File.Move(
                filePath,
                quarantinePath);
        }
        catch
        {
        }
    }

    public bool SetRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(
                    StartupRegistryKey,
                    true);

            if (key == null)
                return false;

            if (enabled)
            {
                string exePath =
                    Environment.ProcessPath ?? "";

                if (string.IsNullOrWhiteSpace(exePath))
                    return false;

                key.SetValue(
                    StartupValueName,
                    $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(
                    StartupValueName,
                    false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
