using System.Text.Json;
using GamepadApp.Models;
using System.IO;
using System.Text;

namespace GamepadApp.Services;

public class ProfileService
{
    private readonly string filePath;
    private readonly string backupPath;

    public ProfileService()
        : this(GetDefaultFilePath())
    {
    }

    public ProfileService(string storageFilePath)
    {
        if (string.IsNullOrWhiteSpace(storageFilePath))
            throw new ArgumentException(
                "Profil dosyası yolu boş olamaz.",
                nameof(storageFilePath));

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

        return Path.Combine(appData, "GamepadApp", "profiles.json");
    }

    public List<Profile> LoadProfiles()
    {
        if (!File.Exists(filePath))
        {
            if (TryReadProfiles(backupPath, out List<Profile>? backupProfiles))
            {
                RestorePrimaryFromBackup();
                return PrepareLoadedProfiles(backupProfiles!);
            }

            return CreateDefaultProfiles();
        }

        if (TryReadProfiles(filePath, out List<Profile>? profiles))
        {
            return PrepareLoadedProfiles(profiles!);
        }

        if (TryReadProfiles(backupPath, out List<Profile>? recoveredProfiles))
        {
            RestorePrimaryFromBackup();
            return PrepareLoadedProfiles(recoveredProfiles!);
        }

        QuarantineCorruptPrimary();
        return CreateDefaultProfiles();
    }

    private List<Profile> PrepareLoadedProfiles(List<Profile> profiles)
    {
        if (profiles.Count == 0)
            return CreateDefaultProfiles();

        List<Profile> loadedProfiles = profiles
            .Where(profile => profile != null)
            .ToList();

        foreach (Profile profile in loadedProfiles)
        {
            profile.ControllerSettings ??= new ControllerProfileSettings();
            profile.ControllerSettings.ButtonMappings ??=
                new Dictionary<string, string>();
            profile.LightbarColors ??= new Profile().LightbarColors;

            if (string.IsNullOrWhiteSpace(profile.Language))
                profile.Language = "TR";
        }

        MigrateLegacyControllerSettings(loadedProfiles);

        return loadedProfiles.Count > 0
            ? loadedProfiles
            : CreateDefaultProfiles();
    }

    private static List<Profile> CreateDefaultProfiles()
    {
        return new List<Profile>
        {
            new Profile
            {
                Name = "Default",
                IsMainProfile = true,
                AdvancedSettingsInitialized = true,
                ControllerSettingsInitialized = true
            }
        };
    }

    public void SaveProfiles(List<Profile> profiles)
    {
        string json = JsonSerializer.Serialize(
            profiles,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = filePath + "." +
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
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
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

    private void ReplaceWithMoveFallback(string temporaryPath)
    {
        if (File.Exists(filePath))
            File.Copy(filePath, backupPath, overwrite: true);

        File.Move(temporaryPath, filePath, overwrite: true);
    }

    private static bool TryReadProfiles(
        string path,
        out List<Profile>? profiles)
    {
        profiles = null;

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            profiles = JsonSerializer.Deserialize<List<Profile>>(json);
            return profiles != null && profiles.Count > 0;
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
            File.Copy(backupPath, filePath, overwrite: true);
        }
        catch
        {
            // Yedek bellekte başarıyla okundu; disk onarımı başarısız olsa bile
            // kullanıcı profilleri bu oturumda kaybolmaz.
        }
    }

    private void QuarantineCorruptPrimary()
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            string directory = Path.GetDirectoryName(filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            string quarantinePath = Path.Combine(
                directory,
                $"{name}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}" +
                $"-{Guid.NewGuid():N}{extension}");

            File.Move(filePath, quarantinePath);
        }
        catch
        {
        }
    }

    public void SaveProfile(Profile profile)
    {
        List<Profile> profiles = LoadProfiles();
        int profileIndex = profiles.FindIndex(
            item => item.Name == profile.Name);

        if (profileIndex < 0)
            return;

        profiles[profileIndex] = profile;
        SaveProfiles(profiles);
    }

    private void MigrateLegacyControllerSettings(
        List<Profile> profiles)
    {
        if (profiles.All(profile =>
                profile.ControllerSettingsInitialized))
        {
            return;
        }

        List<LegacyBindingProfile> legacyProfiles =
            LoadLegacyBindingProfiles();

        bool changed = false;

        foreach (Profile profile in profiles)
        {
            if (profile.ControllerSettingsInitialized)
                continue;

            LegacyBindingProfile? legacyProfile =
                legacyProfiles.FirstOrDefault(item =>
                    item.Name == profile.Name) ??
                legacyProfiles.FirstOrDefault(item =>
                    item.Name == "Default");

            if (legacyProfile != null)
            {
                profile.ControllerSettings.Deadzone =
                    legacyProfile.Deadzone;
                profile.ControllerSettings.AntiDeadzone =
                    legacyProfile.AntiDeadzone;
                profile.ControllerSettings.Sensitivity =
                    legacyProfile.Sensitivity;
                profile.ControllerSettings.ButtonMappings =
                    new Dictionary<string, string>(
                        legacyProfile.ButtonMappings ??
                        new Dictionary<string, string>());
            }

            profile.ControllerSettingsInitialized = true;
            changed = true;
        }

        if (changed)
            SaveProfiles(profiles);
    }

    private static List<LegacyBindingProfile> LoadLegacyBindingProfiles()
    {
        string legacyPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "aRacnid",
            "binding_profiles.json");

        if (!File.Exists(legacyPath))
            return new List<LegacyBindingProfile>();

        try
        {
            string json = File.ReadAllText(legacyPath);
            return JsonSerializer.Deserialize<List<LegacyBindingProfile>>(
                json) ?? new List<LegacyBindingProfile>();
        }
        catch
        {
            return new List<LegacyBindingProfile>();
        }
    }

    private sealed class LegacyBindingProfile
    {
        public string Name { get; set; } = "";
        public double Deadzone { get; set; } = 10;
        public double AntiDeadzone { get; set; }
        public double Sensitivity { get; set; } = 100;
        public Dictionary<string, string> ButtonMappings { get; set; } = new();
    }
}
