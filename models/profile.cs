namespace GamepadApp.Models;

public class ControllerProfileSettings
{
    public double Deadzone { get; set; } = 10;
    public double AntiDeadzone { get; set; }
    public double Sensitivity { get; set; } = 100;
    public double LeftMotorStrength { get; set; } = 100;
    public double RightMotorStrength { get; set; } = 100;
    public string OutputGamepadType { get; set; } = "DualShock4";
    public Dictionary<string, string> ButtonMappings { get; set; } = new();

    public ControllerProfileSettings Clone()
    {
        return new ControllerProfileSettings
        {
            Deadzone = Deadzone,
            AntiDeadzone = AntiDeadzone,
            Sensitivity = Sensitivity,
            LeftMotorStrength = LeftMotorStrength,
            RightMotorStrength = RightMotorStrength,
            OutputGamepadType = OutputGamepadType,
            ButtonMappings = new Dictionary<string, string>(
                ButtonMappings ?? new Dictionary<string, string>())
        };
    }
}

public class Profile
{
    public string Name { get; set; } = "";

    public bool IsMainProfile { get; set; }

    // Profilin Lightbar renk slotları.
    // HEX olarak tutuluyor ki profiles.json içinde temiz kaydedilsin.
    public List<string> LightbarColors { get; set; } =
    [
        "#FF0000", // Kırmızı
        "#0000FF", // Mavi
        "#00FF00", // Yeşil
        "#FFFF00"  // Sarı
    ];

    // En son seçilen renk slotu.
    // Başlangıçta 1 = Mavi.
    public int SelectedLightbarColorIndex { get; set; } = 1;

    public bool LightbarEnabled { get; set; } = true;

    // Gelişmiş ayarlar artık kullanıcı profiline aittir.
    public bool RunAtStartup { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowConnectionNotifications { get; set; } = true;
    public string Language { get; set; } = "TR";

    // Eski settings.json değerlerinin yalnız bir kez taşınması için kullanılır.
    public bool AdvancedSettingsInitialized { get; set; }

    // Eski binding_profiles.json değerlerinin yalnız bir kez taşınması için.
    public bool ControllerSettingsInitialized { get; set; }

    public ControllerProfileSettings ControllerSettings { get; set; } = new();
}
