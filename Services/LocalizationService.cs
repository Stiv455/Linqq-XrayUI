using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinqqXrayVPN.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _strings = new();

        public event EventHandler? LanguageChanged;

        public string this[string key] =>
            _strings.TryGetValue(key, out var value) ? value : $"[{key}]";

        public string GetString(string key) => this[key];

        public async Task LoadLanguageAsync(string languageCode)
        {
            _strings.Clear();

            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "Strings", $"{languageCode}.json");

                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(AppContext.BaseDirectory, "Strings", "en-US.json");
                }

                var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

                _strings = JsonSerializer.Deserialize(
                    json,
                    AppJsonSerializerContext.Default.DictionaryStringString)
                    ?? new Dictionary<string, string>();

                LanguageChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"[Localization] Loaded: {languageCode} ({_strings.Count} strings)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Localization] Error: {ex.Message}");
            }
        }
    }
}