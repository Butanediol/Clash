using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ClashXW
{
    public static class LocalizationManager
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "ClashXW", 
            "settings.json"
        );

        private static CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

        public static event EventHandler? LanguageChanged;

        public static void Initialize()
        {
            // Load saved language preference
            var savedLanguage = LoadLanguagePreference();
            if (!string.IsNullOrEmpty(savedLanguage))
            {
                SetLanguage(savedLanguage);
            }
            else
            {
                // Use system language if available, otherwise default to English
                var systemLang = CultureInfo.CurrentUICulture.Name;
                if (systemLang.StartsWith("zh"))
                {
                    SetLanguage("zh-CN");
                }
                else
                {
                    SetLanguage("en");
                }
            }
        }

        public static void SetLanguage(string cultureName)
        {
            try
            {
                var culture = new CultureInfo(cultureName);
                _currentCulture = culture;
                
                // Update resource manager culture
                Resources.Strings.Culture = culture;
                
                // Save preference
                SaveLanguagePreference(cultureName);
                
                // Notify listeners
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set language: {ex.Message}");
            }
        }

        public static string GetString(string key)
        {
            try
            {
                var value = Resources.Strings.ResourceManager.GetString(key, _currentCulture);
                return value ?? key;
            }
            catch
            {
                return key;
            }
        }

        public static string GetString(string key, params object[] args)
        {
            try
            {
                var format = GetString(key);
                return string.Format(format, args);
            }
            catch
            {
                return key;
            }
        }

        private static string? LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (settings != null && settings.TryGetValue("language", out var lang))
                    {
                        return lang;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SaveLanguagePreference(string cultureName)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new Dictionary<string, string>();
                
                // Load existing settings if file exists
                if (File.Exists(SettingsFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(SettingsFilePath);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (existing != null)
                        {
                            settings = existing;
                        }
                    }
                    catch { }
                }

                settings["language"] = cultureName;
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        public static string CurrentLanguage => _currentCulture.Name;
    }
}
