using System.Text.Json;
using System.IO;
using System.Reflection;

namespace GamepadApp.Services;

public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new());
    public static LocalizationService Instance => _instance.Value;

    private Dictionary<string, string> _strings = new();
    private string _currentLanguage = "TR";

    public string CurrentLanguage => _currentLanguage;

    public event Action? LanguageChanged;

    private LocalizationService()
    {
        LoadLanguage(_currentLanguage);
    }

    public string Get(string key)
    {
        return _strings.TryGetValue(key, out string? value)
            ? value
            : key;
    }

    public string Get(string key, params object[] args)
    {
        string template = Get(key);
        return string.Format(template, args);
    }

    public void SetLanguage(string lang)
    {
        if (_currentLanguage == lang)
            return;

        LoadLanguage(lang);
    }

    private void LoadLanguage(string lang)
    {
        _currentLanguage = lang;

        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = $"GamepadApp.Localization.{lang.ToLower()}.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            _strings = new Dictionary<string, string>();
            return;
        }

        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        _strings = dict ?? new Dictionary<string, string>();

        LanguageChanged?.Invoke();
    }
}
