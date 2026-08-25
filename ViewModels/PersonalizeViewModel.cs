using LinqqXrayVPN.Helpers;
using LinqqXrayVPN.Models;
using LinqqXrayVPN.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace LinqqXrayVPN.ViewModels
{
    public record LanguageOption(string Code, string DisplayName, string NativeName);
    public partial class PersonalizeViewModel : ObservableObject
    {
        private readonly SettingsService _settings;

        private Color _ssColor;
        private Color _vlessColor;
        private Color _vmessColor;
        private Color _hysteria2Color;
        private Color _fallbackColor;

        private int _selectedLanguageIndex;
        private int _selectedThemeIndex;
        private int _selectedBackdropIndex;
        private bool _showLatencyInDetails = true;
        private bool _showUnlockInDetails = true;
        public XamlRoot? CurrentXamlRoot { get; set; }
        public List<LanguageOption> SupportedLanguages { get; } = new()
            {
                new LanguageOption("en-US", "English", "English"),
                new LanguageOption("ru-RU", "Русский", "Русский")
            };

        public event EventHandler? CloseRequested;
        public LocalizationService Loc => LocalizationService.Instance;

        public PersonalizeViewModel(SettingsService settings)
        {
            _settings = settings;
        }

        // ── Colors ────────────────────────────────────────────────────────────

        public Color SsColor
        {
            get => _ssColor;
            set
            {
                if (SetProperty(ref _ssColor, value))
                {
                    ProtocolColorStore.Ss = value;
                    ProtocolColorStore.NotifyColorsChanged();
                }
            }
        }

        public Color VlessColor
        {
            get => _vlessColor;
            set
            {
                if (SetProperty(ref _vlessColor, value))
                {
                    ProtocolColorStore.Vless = value;
                    ProtocolColorStore.NotifyColorsChanged();
                }
            }
        }

        public Color VmessColor
        {
            get => _vmessColor;
            set
            {
                if (SetProperty(ref _vmessColor, value))
                {
                    ProtocolColorStore.Vmess = value;
                    ProtocolColorStore.NotifyColorsChanged();
                }
            }
        }

        public Color Hysteria2Color
        {
            get => _hysteria2Color;
            set
            {
                if (SetProperty(ref _hysteria2Color, value))
                {
                    ProtocolColorStore.Hysteria2 = value;
                    ProtocolColorStore.NotifyColorsChanged();
                }
            }
        }

        public Color FallbackColor
        {
            get => _fallbackColor;
            set
            {
                if (SetProperty(ref _fallbackColor, value))
                {
                    ProtocolColorStore.Fallback = value;
                    ProtocolColorStore.NotifyColorsChanged();
                }
            }
        }

        // ── Theme ─────────────────────────────────────────────────────────────
        // Bound TwoWay to CommunityToolkit Segmented.SelectedIndex.
        // 0 = Light, 1 = Dark, 2 = System/Default

        public int SelectedThemeIndex
        {
            get => _selectedThemeIndex;
            set
            {
                if (!SetProperty(ref _selectedThemeIndex, value)) return;
                var theme = value switch
                {
                    0 => ElementTheme.Light,
                    1 => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
                ThemeHelper.ApplyTheme(theme);
            }
        }

        // ── Backdrop ──────────────────────────────────────────────────────────

        public int SelectedBackdropIndex
        {
            get => _selectedBackdropIndex;
            set
            {
                if (!SetProperty(ref _selectedBackdropIndex, value)) return;
                ThemeHelper.ApplyBackdrop(value == 1 ? "Acrylic" : "Mica");
            }
        }

        public bool ShowLatencyInDetails
        {
            get => _showLatencyInDetails;
            set => SetProperty(ref _showLatencyInDetails, value);
        }

        public bool ShowUnlockInDetails
        {
            get => _showUnlockInDetails;
            set => SetProperty(ref _showUnlockInDetails, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void ResetColors()
        {
            SsColor        = Color.FromArgb(255,  96, 165, 250);
            VlessColor     = Color.FromArgb(255,  52, 211, 153);
            VmessColor     = Color.FromArgb(255, 167, 139, 250);
            Hysteria2Color = Color.FromArgb(255, 251, 146,  60);
            FallbackColor  = Color.FromArgb(255, 148, 163, 184);
        }

        public Task<string> ExportPresetAsync() =>
            new PresetExportService(_settings).ExportAsync();

        [RelayCommand]
        public async Task Done()
        {
            var s = await _settings.LoadSettingsAsync();

            string oldLanguage = s.Language ?? "en-US";

            ProtocolColorStore.SaveTo(s);
            s.ThemeSetting = ThemeHelper.CurrentTheme switch
            {
                ElementTheme.Light => "Light",
                ElementTheme.Dark => "Dark",
                _ => "Default"
            };
            s.BackdropSetting = ThemeHelper.CurrentBackdrop;
            s.ShowLatencyInDetails = ShowLatencyInDetails;
            s.ShowUnlockInDetails = ShowUnlockInDetails;

            if (_selectedLanguageIndex >= 0 && _selectedLanguageIndex < SupportedLanguages.Count)
            {
                s.Language = SupportedLanguages[_selectedLanguageIndex].Code;
            }

            await _settings.SaveSettingsAsync(s);
            bool languageChanged = !string.Equals(oldLanguage, s.Language, StringComparison.OrdinalIgnoreCase);
            if (languageChanged)
            {
                var restart = await ShowRestartDialogAsync();
                if (restart)
                    return;
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        // ── Initialization ────────────────────────────────────────────────────

        public void LoadFromStore()
        {
            var s = _settings.LoadSettingsAsync().Result;
            _ssColor        = ProtocolColorStore.Ss;
            _vlessColor     = ProtocolColorStore.Vless;
            _vmessColor     = ProtocolColorStore.Vmess;
            _hysteria2Color = ProtocolColorStore.Hysteria2;
            _fallbackColor  = ProtocolColorStore.Fallback;

            OnPropertyChanged(nameof(SsColor));
            OnPropertyChanged(nameof(VlessColor));
            OnPropertyChanged(nameof(VmessColor));
            OnPropertyChanged(nameof(Hysteria2Color));
            OnPropertyChanged(nameof(FallbackColor));

            _selectedThemeIndex = ThemeHelper.CurrentTheme switch
            {
                ElementTheme.Light => 0,
                ElementTheme.Dark  => 1,
                _                  => 2,
            };
            OnPropertyChanged(nameof(SelectedThemeIndex));

            _selectedBackdropIndex = ThemeHelper.CurrentBackdrop == "Acrylic" ? 1 : 0;
            OnPropertyChanged(nameof(SelectedBackdropIndex));

            var lang = _settings.LoadSettingsAsync().Result.Language ?? string.Empty;
            _selectedLanguageIndex = SupportedLanguages.FindIndex(l => l.Code == lang);
            if (_selectedLanguageIndex < 0) _selectedLanguageIndex = 0;
            OnPropertyChanged(nameof(SelectedLanguageIndex));
        }

        public void LoadDisplayOptions(AppSettings settings)
        {
            ShowLatencyInDetails = settings.ShowLatencyInDetails;
            ShowUnlockInDetails = settings.ShowUnlockInDetails;
        }

        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set
            {
                if (SetProperty(ref _selectedLanguageIndex, value))
                {
                    var newLang = SupportedLanguages[value].Code;
                }
            }
        }
        private async Task<bool> ShowRestartDialogAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = CurrentXamlRoot,
                Title = Loc.GetString("set17.26"),
                Content = Loc.GetString("set17.27"),
                PrimaryButtonText = Loc.GetString("set17.28"),
                SecondaryButtonText = Loc.GetString("set17.29"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return false;

            RestartApplication();
            return true;
        }

        private static void RestartApplication()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Environment.Exit(0);
                    return;
                }

                var currentPid = Environment.ProcessId;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--parent-pid={currentPid}",
                    UseShellExecute = true
                });

                _ = Task.Run(async () =>
                {
                    await Task.Delay(800);
                    try
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                    catch
                    {
                    }
                });

                if (Application.Current is App app)
                    app.RequestShutdown();
                else
                    Environment.Exit(0);
            }
            catch
            {
                Environment.Exit(0);
            }
        }

    }
}
